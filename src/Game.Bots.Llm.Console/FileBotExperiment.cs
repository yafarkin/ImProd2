using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Bots.Llm;
using Game.Bots.Llm.ConsoleApp;
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
        // Верхняя граница TurnLimit — только sanity-проверка формы; настоящий бюджет и горизонт
        // считает счётчик exchange.py (--limit/--max-response/--max-turns при init по этому же
        // каталогу). Числа должны быть согласованы до старта — см. docs/ASSISTANT-BOT.md.
        if (settings.TurnLimit is < 1 or > 120 || settings.MaxActions < 1 || settings.WaitSeconds < 1) throw new ArgumentException("Invalid file-bot settings.");
        var directoryPath = Path.GetFullPath(settings.Directory);
        Directory.CreateDirectory(directoryPath);
        if (Directory.EnumerateFileSystemEntries(directoryPath).Any()) throw new IOException("Use a fresh directory.");
        await File.WriteAllTextAsync(Path.Combine(directoryPath, "settings.json"), JsonSerializer.Serialize(settings));
        var config = GameConfigLoader.LoadFromFiles(Path.Combine(AppContext.BaseDirectory,"Samples","production-models",settings.ProductionModel),
            Path.Combine(AppContext.BaseDirectory,"Samples","sessions","main.json"));
        if (config.Sectors.Count != 1) throw new ArgumentException("Single-sector test only.");
        var log = DurableEventLog<GameSessionState>.Open(Path.Combine(directoryPath,"journal.jsonl"),Path.Combine(directoryPath,"snapshot.json"),()=>new GameSessionState(config));
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(log,config.Raw.Duration.MaxTurns,[new TeamSpec { Id=teamId,Name="Assistant",SectorId=config.Sectors[0].Id }]);
        var costs = MaterialCostCalculator.CalculateAll(config);
        using var metrics = new StreamWriter(Path.Combine(directoryPath,"metrics.jsonl")) { AutoFlush=true };
        var (previous, completed) = await FileBotTurnLoop.RunDecisionLoopAsync(
            session, teamId, config, directoryPath, settings, costs, firstTurn: 1, lastTurn: settings.TurnLimit, metrics, previous: []);
        var stopReason = completed == settings.TurnLimit ? "turn-limit" : "stopped-before-completion";
        var final = session.State.Teams[teamId];
        await File.WriteAllTextAsync(Path.Combine(directoryPath,"result.json"),JsonSerializer.Serialize(new
        { stopReason,completedTurns=completed,gameFinished=session.State.IsFinished,final.Balance,score=FinalScoreCalculator.Calculate(final,costs,config.Raw.FactoryDefinitions),journalIntegrity=log.VerifyIntegrity() }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault }));
        Console.WriteLine($"STOP {stopReason}: {completed} decisions");
    }
    // internal: тот же вид настроек читают FileBotResumeRunner и FileBotContinueRunner — settings.json
    // каталога прогона остаётся единым описанием каталога; продолжение стартует с новым TurnLimit.
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
