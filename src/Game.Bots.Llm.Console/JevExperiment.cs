using System.Text.Json;
using Game.Bots.Llm.Jev;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Bots.Llm.ConsoleApp;

internal static class JevExperiment
{
    public static async Task RunAsync(string settingsPath)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(settingsPath), jsonOptions)
            ?? throw new InvalidOperationException("Empty experiment settings.");
        settings.Policy.Validate();
        if (settings.Turns < 1 || settings.TimeoutSeconds <= 0 || settings.MaxConsecutiveFailures < 1) throw new ArgumentException("Invalid run duration/timeout.");
        var config = GameConfigLoader.LoadFromFiles(
            Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", settings.ProductionModel),
            Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));
        if (config.Sectors.Count != 1) throw new ArgumentException("This experiment supports one sector only.");
        // Never overwrite another run, including after a failed/cancelled run.
        Directory.CreateDirectory(settings.OutputDirectory);
        var journalPath = Path.Combine(settings.OutputDirectory, "journal.jsonl");
        if (Directory.EnumerateFileSystemEntries(settings.OutputDirectory).Any())
            throw new IOException("Output directory must be empty; use a fresh directory per experiment.");
        await File.WriteAllTextAsync(Path.Combine(settings.OutputDirectory, "settings.json"), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        var log = DurableEventLog<GameSessionState>.Open(journalPath, Path.Combine(settings.OutputDirectory, "snapshot.json"), () => new GameSessionState(config));
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(log, settings.Turns, [new TeamSpec { Id = teamId, Name = "JEV", SectorId = config.Sectors[0].Id }]);
        using var http = new HttpClient { BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) };
        var bot = new JevBot(new JevClient(http, settings.Model), settings.Policy);
        var random = new Random(settings.Seed);
        var materialCosts = MaterialCostCalculator.CalculateAll(config);
        var producedByMaterial = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        using var decisions = new StreamWriter(Path.Combine(settings.OutputDirectory, "decisions.jsonl")) { AutoFlush = true };
        using var planning = new StreamWriter(Path.Combine(settings.OutputDirectory, "planning.jsonl")) { AutoFlush = true };
        using var metrics = new StreamWriter(Path.Combine(settings.OutputDirectory, "metrics.jsonl")) { AutoFlush = true };
        var failureStreak = 0;
        var stopReason = "completed";
        try
        {
            while (!session.State.IsFinished)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (session.State.CurrentPhase == TurnPhase.Settlement)
                {
                    var firstEntry = session.Entries.Count;
                    session.RunTick(random);
                    foreach (var production in session.Entries.Skip(firstEntry).Select(e => e.Change).OfType<FactoryProduced>())
                    {
                        var material = session.State.Teams[production.TeamId].Factories.Single(f => f.Id == production.FactoryId).SelectedRecipe.Output.Id;
                        producedByMaterial[material] = producedByMaterial.GetValueOrDefault(material) + production.OutputQuantity;
                    }
                    session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
                    continue;
                }
                var failed = await bot.TakeTurnAsync(session, teamId, random, async record =>
                {
                    await decisions.WriteLineAsync(JsonSerializer.Serialize(record));
                    Console.WriteLine($"turn {record.Turn}, step {record.Step}: {record.Selected} [{record.Outcome}], {record.ElapsedMilliseconds:0} ms");
                }, cancellation.Token, async record =>
                {
                    await planning.WriteLineAsync(JsonSerializer.Serialize(record));
                    Console.WriteLine($"turn {record.Turn}, assessment: {record.Answer?.Priority}/{record.Answer?.TargetRecipe}, research={record.Answer?.ResearchNowProbability:0.00} [{record.Outcome}], {record.ElapsedMilliseconds:0} ms");
                });
                failureStreak = failed ? failureStreak + 1 : 0;
                if (failureStreak >= settings.MaxConsecutiveFailures)
                {
                    stopReason = "repeated-service-or-command-failures";
                    break;
                }
                var team = session.State.Teams[teamId];
                await metrics.WriteLineAsync(JsonSerializer.Serialize(new { turn = session.State.CurrentTurn, team.Balance,
                    score = FinalScoreCalculator.Calculate(team, materialCosts, config.Raw.FactoryDefinitions),
                    factories = team.Factories.Count, team.UnlockedGeneration, recipes = team.Factories.Select(f => f.SelectedRecipe.Id).ToArray() }));
                session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
            }
            var final = session.State.Teams[teamId];
            await File.WriteAllTextAsync(Path.Combine(settings.OutputDirectory, "result.json"), JsonSerializer.Serialize(new
            {
                completed = session.State.IsFinished, stopReason, turn = session.State.CurrentTurn, final.Balance,
                score = FinalScoreCalculator.Calculate(final, materialCosts, config.Raw.FactoryDefinitions),
                producedByMaterial,
                factories = final.Factories.Count, final.UnlockedGeneration,
                recipes = final.Factories.Select(f => f.SelectedRecipe.Id).ToArray(),
                journalIntegrity = log.VerifyIntegrity(),
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Stopped ({stopReason}): balance {final.Balance:0.00}, factories {final.Factories.Count}; {settings.OutputDirectory}");
        }
        finally { Console.CancelKeyPress -= cancel; }
        if (!session.State.IsFinished) Environment.ExitCode = 1;
    }

    private sealed record Settings
    {
        public string BaseUrl { get; init; } = "http://192.168.0.2:11434";
        public string Model { get; init; } = "nimble";
        public string ProductionModel { get; init; } = "training-1-sector.json";
        public int Turns { get; init; } = 98;
        public int Seed { get; init; } = 42;
        public int MaxConsecutiveFailures { get; init; } = 3;
        public int TimeoutSeconds { get; init; } = 30;
        public string OutputDirectory { get; init; } = "_bots_llm/jev-experiment";
        public JevBotOptions Policy { get; init; } = new();
    }
}
