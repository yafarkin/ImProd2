using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Bots.Llm;

/// <summary>
/// Мультикомандный прогон файловых ботов на одной сессии (вариант A «три LLM-бота на трёх
/// секторах», согласовано с пользователем 2026-10-21). Класс — здесь (Game.Bots.Llm), как и вся
/// тестируемая логика файлового протокола; консоль только пробрасывает настройку
/// (<c>--file-bot-multi</c>). Один durable-журнал и одна сессия — в базовом каталоге; у каждой
/// команды свой подкаталог обмена (settings.json, {n:000}.request/.response/.approved,
/// metrics.jsonl), как у соло-прогона <c>FileBotExperiment</c>. Счётчик exchange.py
/// инициализируется отдельно по КАЖДОМУ каталогу команды; механика обмена та же. База должна быть
/// свежим каталогом; продолжение остановленного прогона в этот блок не входит.
/// </summary>
public static class FileBotMultiExperiment
{
    public static async Task RunAsync(string settingsPath)
    {
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(settingsPath)) ?? throw new JsonException();
        var config = GameConfigLoader.LoadFromFiles(
            Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", settings.ProductionModel),
            Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));
        await RunCoreAsync(settings, config);
    }

    /// <summary>Публичное ядро без загрузки конфига из Samples — тесты подменяют модель на фикстуру (tiny-3-sectors) и inline-сессионные параметры.</summary>
    public static async Task RunCoreAsync(Settings settings, ResolvedGameConfig config)
    {
        Validate(settings, config);
        var basePath = Path.GetFullPath(settings.Directory);
        Directory.CreateDirectory(basePath);
        if (Directory.EnumerateFileSystemEntries(basePath).Any()) throw new IOException("Use a fresh directory.");
        await File.WriteAllTextAsync(Path.Combine(basePath, "settings.json"), JsonSerializer.Serialize(settings));

        var log = DurableEventLog<GameSessionState>.Open(
            Path.Combine(basePath, "journal.jsonl"), Path.Combine(basePath, "snapshot.json"),
            () => new GameSessionState(config));
        var specs = new List<TeamSpec>();
        foreach (var teamSettings in settings.Teams)
        {
            specs.Add(new TeamSpec { Id = Ulid.NewUlid(), Name = teamSettings.Name, SectorId = teamSettings.SectorId });
        }
        var session = GameSession.StartWithEndTurn(log, config.Raw.Duration.MaxTurns, specs);
        var costs = MaterialCostCalculator.CalculateAll(config);
        var teams = new List<FileBotMultiTurnLoop.TeamLink>();
        var writers = new List<TextWriter>();
        for (var i = 0; i < settings.Teams.Count; i++)
        {
            var teamSettings = settings.Teams[i];
            var directoryPath = Path.GetFullPath(Path.Combine(basePath, teamSettings.Directory));
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "settings.json"), JsonSerializer.Serialize(teamSettings));
            var metrics = new StreamWriter(Path.Combine(directoryPath, "metrics.jsonl")) { AutoFlush = true };
            writers.Add(metrics);
            teams.Add(new FileBotMultiTurnLoop.TeamLink(
                specs[i].Id, teamSettings.Name, $"T{i + 1}", directoryPath, teamSettings.SectorId, teamSettings.Rules, metrics));
        }
        var (completed, stopReason) = await FileBotMultiTurnLoop.RunDecisionLoopAsync(session, config, basePath, settings, costs, teams, firstTurn: 1, lastTurn: settings.TurnLimit);
        var teamResults = teams.Select(t =>
        {
            var team = session.State.Teams[t.Id];
            return new
            {
                name = t.Name,
                sectorId = t.SectorId,
                balance = team.Balance,
                score = FinalScoreCalculator.Calculate(team, costs, config.Raw.FactoryDefinitions),
                factories = team.Factories.Count,
            };
        }).ToArray();
        await File.WriteAllTextAsync(Path.Combine(basePath, "result.json"), JsonSerializer.Serialize(new
        {
            stopReason, completedTurns = completed, gameFinished = session.State.IsFinished, teamResults, journalIntegrity = log.VerifyIntegrity(),
        }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault }));
        foreach (var writer in writers) await writer.DisposeAsync();
        Console.WriteLine($"STOP {stopReason}: {completed} decisions");
    }

    private static void Validate(Settings settings, ResolvedGameConfig config)
    {
        // Верхняя граница TurnLimit — sanity формы, настоящий бюджет считает счётчик exchange.py
        // (--limit/--max-turns при init по КАЖДОМУ каталогу команды); числа согласуются до старта.
        if (settings.TurnLimit is < 1 or > 120 || settings.MaxActions < 1 || settings.WaitSeconds < 1)
            throw new ArgumentException("Invalid file-bot-multi settings.");
        if (settings.TurnLimit > config.Raw.Duration.MaxTurns)
            throw new ArgumentException("TurnLimit exceeds the session's MaxTurns.");
        if (settings.Teams.Count < 2)
            throw new ArgumentException("A multi-bot run needs at least two teams.");
        if (settings.Teams.Select(t => t.Directory).Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Every team needs a subdirectory.");
        if (settings.Teams.Select(t => t.Directory).ToHashSet(StringComparer.Ordinal).Count != settings.Teams.Count)
            throw new ArgumentException("Team subdirectories must be unique.");
        if (settings.Teams.Select(t => t.SectorId).ToHashSet(StringComparer.Ordinal).Count != settings.Teams.Count)
            throw new ArgumentException("A multi-sector market run needs distinct sectors per team.");
        var knownSectors = config.Sectors.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var team in settings.Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Name)) throw new ArgumentException("Every team needs a name.");
            if (!knownSectors.Contains(team.SectorId)) throw new ArgumentException($"Unknown sector '{team.SectorId}'.");
        }
    }

    // internal: файл настроек базового каталога (settings.json) — единое описание прогона; для
    // мультипрогона resume/continue не реализованы, но запись нужна воспроизводимости архива.
    public sealed record Settings
    {
        public string Directory { get; init; } = "_bots_llm/multi-3sectors";
        public string ProductionModel { get; init; } = "main-3-sectors.json";
        public int TurnLimit { get; init; } = 40;
        public int Seed { get; init; } = 42;
        public int MaxActions { get; init; } = 14;
        public int WaitSeconds { get; init; } = 600;
        public IReadOnlyList<TeamSettings> Teams { get; init; } = [];

        public sealed record TeamSettings
        {
            public string Name { get; init; } = "";
            public string Directory { get; init; } = ""; // относительно базового каталога
            public string SectorId { get; init; } = "";
            public string Rules { get; init; } = ""; // персона/стратегия именно ЭТОЙ команды
        }
    }
}
