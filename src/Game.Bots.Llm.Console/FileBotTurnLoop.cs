using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Bots.Llm;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm.ConsoleApp;

/// <summary>
/// Совместный цикл решений файлового бота (docs/ASSISTANT-BOT.md) для двух входов: нового прогона
/// (<see cref="FileBotExperiment.RunAsync"/>) и продолжения остановленного
/// (<c>FileBotContinueRunner</c>, пользователь договорился о новом горизонте до старта — это
/// свойство настроек TurnLimit, а не слово «продолжи»). Исполняет решения в фазе Decision заданных
/// ходов поверх живой сессии и durable-журнала; запросы/ответы/метрики — в тот же каталог. Контекст
/// (правила, экономика, каталог) отправляется только на первом ходе нового прогона — продолжение
/// предполагает, что игрок его уже видел и хранил у себя; tenth-turn правилом счетчика это только
/// гарантируется.
/// </summary>
internal static class FileBotTurnLoop
{
    public static async Task<(IReadOnlyList<string> Previous, int Completed)> RunDecisionLoopAsync(
        GameSession session, Ulid teamId, ResolvedGameConfig config, string directoryPath,
        FileBotExperiment.Settings settings, IReadOnlyDictionary<string, decimal> costs,
        int firstTurn, int lastTurn, TextWriter metrics, IReadOnlyList<string> previous)
    {
        var random = new Random(settings.Seed);
        var wire = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var completed = 0;
        for (var n = firstTurn; n <= lastTurn; n++)
        {
            session.RunTick(random);
            session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
            var team = session.State.Teams[teamId];
            var factories = team.Factories.OrderBy(f => f.Id).ToArray();
            var aliases = factories.Select((f, i) => (Alias: $"f{i + 1}", f.Id)).ToDictionary(x => x.Alias, x => x.Id);
            static decimal R(decimal value) => decimal.Round(value, 2);
            var requestId = $"turn-{n}";
            var request = new
            {
                requestId, turn = session.State.CurrentTurn, balance = R(team.Balance), generation = team.UnlockedGeneration,
                research = new { invested = team.GenerationResearchInvestment, perTurn = team.GenerationResearchCommitmentPerTurn,
                    cap = config.Raw.GenerationResearch.MaxCommitmentPerTurn, points = GenerationResearchCalculator.CalculateResearchPoints(team.GenerationResearchInvestment, config.Raw.GenerationResearch) },
                rules = n == 1 ? $"Develop a profitable chain over the public {config.Raw.Duration.MinTurns}-{config.Raw.Duration.MaxTurns} turn game horizon. This test samples at most {settings.TurnLimit} decisions. Display numbers rounded to two decimals. Negative cash allowed. Tick: hire (raw instant, others ramp), wear, lower-to-higher production, sales. Sales last one tick, research/staff persist. Responses: requestId + actions array. Use existing BotCommand kinds buildFactory(factoryDefinitionId), setWorkerCount(factory,count), sellToSystem(materialId,volume), setGenerationResearchCommitment(amount), setOverhaulRequested(factory,enabled). factory=f1 etc; new:TYPE targets latest factory of that type built earlier in this response. No automatic hiring. Empty actions allowed. No reasoning text needed." : null,
                economy = n == 1 ? new { config.Raw.WorkerProductivity, config.Raw.GenerationResearch, config.Raw.Warehouse, config.Raw.Wear } : null,
                catalogue = n == 1 ? config.FactoryDefinitions.OrderBy(d => d.Recipes[0].Output.Level).ThenBy(d => d.Id, StringComparer.Ordinal).Select(d => new object[]
                {d.Id, d.Recipes[0].Output.Level, config.Raw.FactoryDefinitions.Single(x => x.Id == d.Id).BuildCost,
                    config.Raw.FactoryDefinitions.Single(x => x.Id == d.Id).FixedCostPerTurn,
                    d.Recipes.Select(r => new { output = r.Output.Id, rate = r.ProductionRate, inputs = r.Inputs.Select(i => new object[]{i.Material.Id, i.Quantity/r.OutputQuantity}).ToArray() }).ToArray() }).ToArray() : null,
                columns = n == 1 ? "catalogue=[type,requiredGeneration,buildCost,fixedCostPerTurn,recipes]; factories=[alias,type,workers,desired,condition,repairTurns]; market=[material,price,capacity]; stock=[material,quantity]." : null,
                factories = factories.Select((f, i) => new object[]{$"f{i + 1}", f.Definition.Id, f.Workers, f.DesiredWorkers, R(f.Condition), f.RepairTurnsRemaining}).ToArray(),
                stock = team.Warehouse.Stock.Where(s => s.Quantity > 0).OrderBy(s => s.Material.Id, StringComparer.Ordinal).Select(s => new object[]{s.Material.Id, R(s.Quantity)}).ToArray(),
                market = config.Materials.Values.OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => new object[]{m.Id, R(session.State.Market.QuoteOf(m.Id).Price), R(session.State.Market.QuoteOf(m.Id).Capacity)}).ToArray(),
                previousResults = previous,
            };
            var stem = Path.Combine(directoryPath, $"{n:000}");
            await File.WriteAllTextAsync(stem + ".request.json", JsonSerializer.Serialize(request, wire));
            Console.WriteLine($"WAIT {n:000}");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(settings.WaitSeconds);
            while (!File.Exists(stem + ".approved") && !File.Exists(Path.Combine(directoryPath, "STOP")) && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(200);
            if (File.Exists(Path.Combine(directoryPath, "STOP"))) return (previous, completed);
            if (!File.Exists(stem + ".approved")) return (previous, completed);
            var response = await File.ReadAllTextAsync(stem + ".response.json");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response)));
            if ((await File.ReadAllTextAsync(stem + ".approved")).Trim() != hash) throw new InvalidOperationException("Unapproved response bytes.");
            previous = FileBotProtocol.Execute(response, requestId, session, teamId, aliases, random, settings.MaxActions);
            completed++;
            await metrics.WriteLineAsync(JsonSerializer.Serialize(new { turn = session.State.CurrentTurn, team.Balance,
                score = FinalScoreCalculator.Calculate(team, costs, config.Raw.FactoryDefinitions), factories = team.Factories.Count, team.UnlockedGeneration, results = previous }));
            Console.WriteLine($"DONE {n:000}: {string.Join(';', previous)}");
            if (n < lastTurn) session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        }
        return (previous, completed);
    }
}
