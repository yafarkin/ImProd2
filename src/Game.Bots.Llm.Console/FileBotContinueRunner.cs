using System.Text.Json;
using Game.Bots.Llm;
using Game.Bots.Llm.ConsoleApp;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

namespace Game.Bots.Llm.ConsoleApp;

/// <summary>
/// Продолжение нового прогона файлового бота на расширенный горизонт (браузер прежних опытов:
/// docs/ASSISTANT-BOT.md). Восстанавливает сессию из durable-журнала, проверяет согласованность
/// (фаза Decision, ни одного ожидающего одобренного ответа на текущий ход, metrics.jsonl покрывает
/// все исполненные решения) и гонит тот же цикл решений ходов [текущий+1 .. TurnLimit]. Это не
/// resume ответа: решение текущего хода уже исполнено, ожидаемого ответа нет — меняем только
/// горизонт. Бюджет остаётся у счётчика exchange.py; max_turns должен быть поднят там же до
/// TurnLimit (не обнуляя учёт), если прежний горизонт был исчерпан.
/// </summary>
internal static class FileBotContinueRunner
{
    public static async Task RunAsync(string settingsPath)
    {
        var settings = JsonSerializer.Deserialize<FileBotExperiment.Settings>(
            await File.ReadAllTextAsync(settingsPath)) ?? throw new JsonException("Invalid settings file.");
        if (settings.TurnLimit is < 1 or > 120 || settings.MaxActions < 1 || settings.WaitSeconds < 1)
            throw new ArgumentException("Invalid file-bot settings.");
        var directoryPath = Path.GetFullPath(settings.Directory);
        if (!File.Exists(Path.Combine(directoryPath, "journal.jsonl")))
            throw new IOException($"No journal.jsonl in '{directoryPath}' — there is nothing to continue.");

        var config = GameConfigLoader.LoadFromFiles(
            Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", settings.ProductionModel),
            Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));
        if (config.Sectors.Count != 1) throw new ArgumentException("Single-sector test only.");

        var log = DurableEventLog<GameSessionState>.Open(
            Path.Combine(directoryPath, "journal.jsonl"), Path.Combine(directoryPath, "snapshot.json"),
            () => new GameSessionState(config));
        if (!log.VerifyIntegrity()) throw new InvalidOperationException("Journal integrity failed.");
        var session = new GameSession(log);
        if (session.State.IsFinished) throw new InvalidOperationException("The session is already finished.");
        if (session.State.CurrentPhase != TurnPhase.Decision)
            throw new InvalidOperationException(
                $"The session is in phase {session.State.CurrentPhase}; continuation expects a Decision phase without a pending answer.");
        if (session.State.Teams.Count != 1) throw new InvalidOperationException("Single-team test only.");
        var teamId = session.State.Teams.Keys.Single();
        var currentTurn = session.State.CurrentTurn;

        // Отличие от resume: решение текущего хода уже исполнено — это гарантирует проверка
        // metrics.jsonl ниже (число решений == текущему ходу); одобренный файл ответа остаётся
        // на диске как история, и его не нужно исполнять повторно. Если же ответ исполнен не был
        // (metrics отстают), это был бы resume-кейс, и сессия была бы не согласована — бросаем.

        var metricsPath = Path.Combine(directoryPath, "metrics.jsonl");
        if (!File.Exists(metricsPath)) throw new IOException("No metrics.jsonl in the run directory.");
        var completedDecisions = File.ReadLines(metricsPath).Count();
        if (completedDecisions != currentTurn)
            throw new InvalidOperationException(
                $"metrics.jsonl has {completedDecisions} decisions, but the session is at turn {currentTurn}; the run is inconsistent.");

        // Снимок предыдущего итога сохраняется: result.json продолжает описывать точку остановки,
        // итог продолжения — в result-continued.json, история не переписывается.
        var previousResultPath = Path.Combine(directoryPath, "result.json");
        if (File.Exists(previousResultPath))
        {
            var snapshotPath = Path.Combine(directoryPath, $"result-{currentTurn:000}.json");
            if (!File.Exists(snapshotPath))
                File.Copy(previousResultPath, snapshotPath);
        }

        var previousResults = File.ReadLines(metricsPath).Last() is { Length: > 0 } lastLine
            ? JsonSerializer.Deserialize<MetricsLine>(lastLine)?.Results ?? []
            : [];
        var costs = MaterialCostCalculator.CalculateAll(config);
        using var metrics = new StreamWriter(metricsPath, append: true) { AutoFlush = true };
        var (previous, completed) = await FileBotTurnLoop.RunDecisionLoopAsync(
            session, teamId, config, directoryPath, settings, costs,
            firstTurn: currentTurn + 1, lastTurn: settings.TurnLimit, metrics, previous: previousResults);

        var final = session.State.Teams[teamId];
        await File.WriteAllTextAsync(Path.Combine(directoryPath, "result-continued.json"), JsonSerializer.Serialize(new
        {
            stopReason = completed == settings.TurnLimit - currentTurn ? "turn-limit" : "stopped-before-completion",
            completedTurns = currentTurn + completed,
            gameFinished = session.State.IsFinished,
            final.Balance,
            score = FinalScoreCalculator.Calculate(final, costs, config.Raw.FactoryDefinitions),
            journalIntegrity = log.VerifyIntegrity(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CONTINUE-STOP: {currentTurn + completed} decisions total");
    }

    private sealed record MetricsLine(IReadOnlyList<string> Results);
}
