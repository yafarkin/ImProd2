using System.Text.Json;
using Game.Bots.Llm;
using Game.Bots.Llm.ConsoleApp;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Bots.Llm.ConsoleApp;

/// <summary>
/// Возобновление остановленного файлового бота (docs/ASSISTANT-BOT-NEXT-STEP.md). Восстанавливает
/// <see cref="GameSession"/> из durable-журнала каталога прогона, проверяет одобренный счётчиком
/// ответ на текущий ход (<see cref="FileBotResume.FindApproved"/>) и исполняет его через обычный
/// <see cref="FileBotProtocol"/>. Это отложенное исполнение уже одобренного решения: ни расчёта
/// нового тика, ни выдачи запросов модели, ни повторного пересчёта уже рассчитанного хода здесь
/// нет. Случайность не влияет на результат: <see cref="FileBotResume"/> отвергает ответы со
/// случайными командами, а генератор создаётся заново из Seed настроек, как и в исходном прогоне.
/// </summary>
internal static class FileBotResumeRunner
{
    public static async Task RunAsync(string directory)
    {
        var directoryPath = Path.GetFullPath(directory);
        var settingsPath = Path.Combine(directoryPath, "settings.json");
        if (!File.Exists(settingsPath)) throw new IOException($"No settings.json in '{directoryPath}'.");
        var settings = JsonSerializer.Deserialize<FileBotExperiment.Settings>(
            await File.ReadAllTextAsync(settingsPath)) ?? throw new JsonException("Invalid settings.json.");
        var journalPath = Path.Combine(directoryPath, "journal.jsonl");
        if (!File.Exists(journalPath)) throw new IOException($"No journal.jsonl in '{directoryPath}'.");

        var config = GameConfigLoader.LoadFromFiles(
            Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", settings.ProductionModel),
            Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));
        if (config.Sectors.Count != 1) throw new ArgumentException("Single-sector test only.");

        var log = DurableEventLog<GameSessionState>.Open(journalPath,
            Path.Combine(directoryPath, "snapshot.json"), () => new GameSessionState(config));
        if (!log.VerifyIntegrity()) throw new InvalidOperationException("Journal integrity failed.");
        var session = new GameSession(log);
        if (session.State.IsFinished) throw new InvalidOperationException("The saved session is already finished.");
        if (session.State.CurrentPhase != TurnPhase.Decision)
            throw new InvalidOperationException($"The saved session is in phase {session.State.CurrentPhase}; decisions are impossible.");
        if (session.State.Teams.Count != 1)
            throw new InvalidOperationException("Single-team test only.");
        var teamId = session.State.Teams.Keys.Single();
        var team = session.State.Teams[teamId];

        var pending = FileBotResume.FindApproved(directoryPath, session.State.CurrentTurn)
            ?? throw new InvalidOperationException(
                $"No approved response for turn {session.State.CurrentTurn}: the stop happened before the decision was delivered, nothing to execute.");

        var metricsPath = Path.Combine(directoryPath, "metrics.jsonl");
        var completed = File.ReadLines(metricsPath).Count();
        if (completed != pending.Turn - 1)
            throw new InvalidOperationException(
                $"metrics.jsonl has {completed} decisions, but turn {pending.Turn} is pending; the run is inconsistent.");

        var aliases = team.Factories.OrderBy(f => f.Id).Select((f, i) => (Alias: $"f{i + 1}", f.Id))
            .ToDictionary(x => x.Alias, x => x.Id);
        var random = new Random(settings.Seed);
        var costs = MaterialCostCalculator.CalculateAll(config);
        var previous = FileBotProtocol.Execute(pending.ResponseJson, $"turn-{pending.Turn}",
            session, teamId, aliases, random, settings.MaxActions);

        await File.AppendAllTextAsync(metricsPath, JsonSerializer.Serialize(new
        {
            turn = session.State.CurrentTurn, team.Balance,
            score = FinalScoreCalculator.Calculate(team, costs, config.Raw.FactoryDefinitions),
            factories = team.Factories.Count, team.UnlockedGeneration, results = previous,
        }) + "\n");

        var final = session.State.Teams[teamId];
        await File.WriteAllTextAsync(Path.Combine(directoryPath, "result-resumed.json"), JsonSerializer.Serialize(new
        {
            stopReason = "resumed-completed",
            resumedTurn = pending.Turn,
            completedTurns = completed + 1,
            gameFinished = session.State.IsFinished,
            final.Balance,
            score = FinalScoreCalculator.Calculate(final, costs, config.Raw.FactoryDefinitions),
            journalIntegrity = log.VerifyIntegrity(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RESUMED {pending.Turn:000}: {string.Join(';', previous)}");
    }
}
