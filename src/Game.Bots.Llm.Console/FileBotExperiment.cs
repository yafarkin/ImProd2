using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Bots.Llm;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Bots.Llm.ConsoleApp;

internal static class FileBotExperiment
{
    public static async Task RunAsync(string settingsPath)
    {
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(settingsPath)) ?? throw new JsonException();
        if (settings.TurnLimit is < 1 or > 10 || settings.MaxActions < 1 || settings.WaitSeconds < 1) throw new ArgumentException("Invalid short test settings.");
        Directory.CreateDirectory(settings.Directory);
        if (Directory.EnumerateFileSystemEntries(settings.Directory).Any()) throw new IOException("Use a fresh directory.");
        await File.WriteAllTextAsync(Path.Combine(settings.Directory, "settings.json"), JsonSerializer.Serialize(settings));
        var config = GameConfigLoader.LoadFromFiles(Path.Combine(AppContext.BaseDirectory,"Samples","production-models",settings.ProductionModel),
            Path.Combine(AppContext.BaseDirectory,"Samples","sessions","main.json"));
        if (config.Sectors.Count != 1) throw new ArgumentException("Single-sector test only.");
        var log = DurableEventLog<GameSessionState>.Open(Path.Combine(settings.Directory,"journal.jsonl"),Path.Combine(settings.Directory,"snapshot.json"),()=>new GameSessionState(config));
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(log,config.Raw.Duration.MaxTurns,[new TeamSpec { Id=teamId,Name="Assistant",SectorId=config.Sectors[0].Id }]);
        var random = new Random(settings.Seed);
        var costs = MaterialCostCalculator.CalculateAll(config);
        IReadOnlyList<string> previous = [];
        var completedTurns = 0;
        var stopReason = "turn-limit";
        var wire = new JsonSerializerOptions { DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull };
        using var metrics = new StreamWriter(Path.Combine(settings.Directory,"metrics.jsonl")) { AutoFlush=true };
        for (var n=1;n<=settings.TurnLimit;n++)
        {
            session.RunTick(random);
            session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
            var team = session.State.Teams[teamId];
            var factories = team.Factories.OrderBy(f=>f.Id).ToArray();
            var aliases = factories.Select((f,i)=>(Alias:$"f{i+1}",f.Id)).ToDictionary(x=>x.Alias,x=>x.Id);
            static decimal R(decimal value)=>decimal.Round(value,2);
            var requestId=$"turn-{n}";
            var request = new
            {
                requestId,turn=session.State.CurrentTurn,balance=R(team.Balance),generation=team.UnlockedGeneration,
                research=new { invested=team.GenerationResearchInvestment,perTurn=team.GenerationResearchCommitmentPerTurn,
                    cap=config.Raw.GenerationResearch.MaxCommitmentPerTurn,points=GenerationResearchCalculator.CalculateResearchPoints(team.GenerationResearchInvestment,config.Raw.GenerationResearch) },
                rules=n==1 ? $"Develop a profitable chain over the public {config.Raw.Duration.MinTurns}-{config.Raw.Duration.MaxTurns} turn game horizon. This test samples at most {settings.TurnLimit} decisions. Display numbers rounded to two decimals. Negative cash allowed. Tick: hire (raw instant, others ramp), wear, lower-to-higher production, sales. Sales last one tick, research/staff persist. Responses: requestId + actions array. Use existing BotCommand kinds buildFactory(factoryDefinitionId), setWorkerCount(factory,count), sellToSystem(materialId,volume), setGenerationResearchCommitment(amount), setOverhaulRequested(factory,enabled). factory=f1 etc; new:TYPE targets latest factory of that type built earlier in this response. No automatic hiring. Empty actions allowed. No reasoning text needed." : null,
                economy=n==1 ? new { config.Raw.WorkerProductivity,config.Raw.GenerationResearch,config.Raw.Warehouse,config.Raw.Wear } : null,
                catalogue=n==1 ? config.FactoryDefinitions.OrderBy(d=>d.Recipes[0].Output.Level).ThenBy(d=>d.Id,StringComparer.Ordinal).Select(d=>new object[]
                {d.Id,d.Recipes[0].Output.Level,config.Raw.FactoryDefinitions.Single(x=>x.Id==d.Id).BuildCost,
                    config.Raw.FactoryDefinitions.Single(x=>x.Id==d.Id).FixedCostPerTurn,
                    d.Recipes.Select(r=>new { output=r.Output.Id,rate=r.ProductionRate,inputs=r.Inputs.Select(i=>new object[]{i.Material.Id,i.Quantity/r.OutputQuantity}).ToArray() }).ToArray() }).ToArray() : null,
                columns=n==1 ? "catalogue=[type,requiredGeneration,buildCost,fixedCostPerTurn,recipes]; factories=[alias,type,workers,desired,condition,repairTurns]; market=[material,price,capacity]; stock=[material,quantity]." : null,
                factories=factories.Select((f,i)=>new object[]{$"f{i+1}",f.Definition.Id,f.Workers,f.DesiredWorkers,R(f.Condition),f.RepairTurnsRemaining}).ToArray(),
                stock=team.Warehouse.Stock.Where(s=>s.Quantity>0).OrderBy(s=>s.Material.Id,StringComparer.Ordinal).Select(s=>new object[]{s.Material.Id,R(s.Quantity)}).ToArray(),
                market=config.Materials.Values.OrderBy(m=>m.Id,StringComparer.Ordinal).Select(m=>new object[]{m.Id,R(session.State.Market.QuoteOf(m.Id).Price),R(session.State.Market.QuoteOf(m.Id).Capacity)}).ToArray(),
                previousResults=previous,
            };
            var stem=Path.Combine(settings.Directory,$"{n:000}");
            await File.WriteAllTextAsync(stem+".request.json",JsonSerializer.Serialize(request,wire));
            Console.WriteLine($"WAIT {n:000}");
            var deadline=DateTimeOffset.UtcNow.AddSeconds(settings.WaitSeconds);
            while (!File.Exists(stem+".approved") && !File.Exists(Path.Combine(settings.Directory,"STOP")) && DateTimeOffset.UtcNow<deadline)
                await Task.Delay(200);
            if (File.Exists(Path.Combine(settings.Directory,"STOP"))) { stopReason="token-budget-or-user-stop";break; }
            if (!File.Exists(stem+".approved")) { stopReason="response-timeout";break; }
            var response=await File.ReadAllTextAsync(stem+".response.json");
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response)));
            if ((await File.ReadAllTextAsync(stem+".approved")).Trim()!=hash) throw new InvalidOperationException("Unapproved response bytes.");
            previous=FileBotProtocol.Execute(response,requestId,session,teamId,aliases,random,settings.MaxActions);
            completedTurns++;
            await metrics.WriteLineAsync(JsonSerializer.Serialize(new { turn=session.State.CurrentTurn,team.Balance,
                score=FinalScoreCalculator.Calculate(team,costs,config.Raw.FactoryDefinitions),factories=team.Factories.Count,team.UnlockedGeneration,results=previous }));
            Console.WriteLine($"DONE {n:000}: {string.Join(';',previous)}");
            if (n<settings.TurnLimit) session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        }
        var final=session.State.Teams[teamId];
        await File.WriteAllTextAsync(Path.Combine(settings.Directory,"result.json"),JsonSerializer.Serialize(new
        { stopReason,completedTurns,gameFinished=session.State.IsFinished,final.Balance,score=FinalScoreCalculator.Calculate(final,costs,config.Raw.FactoryDefinitions),journalIntegrity=log.VerifyIntegrity() }));
        Console.WriteLine($"STOP {stopReason}: {completedTurns} decisions");
    }
    // internal: тот же вид настроек читает FileBotResumeRunner при возобновлении остановленного
    // опыта (docs/ASSISTANT-BOT-NEXT-STEP.md) — settings.json каталога прогона остаётся единым
    // описанием каталога, нового файла настроек для resume не заводится.
    internal sealed record Settings
    {
        public string Directory { get; init; } = "_bots_llm/assistant-01";
        public string ProductionModel { get; init; } = "training-1-sector.json";
        public int TurnLimit { get; init; } = 10;
        public int Seed { get; init; } = 42;
        public int MaxActions { get; init; } = 12;
        public int WaitSeconds { get; init; } = 300;
    }
}
