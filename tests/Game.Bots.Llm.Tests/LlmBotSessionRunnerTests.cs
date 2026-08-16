namespace Game.Bots.Llm.Tests;

/// <summary>Доказывает, что <see cref="LlmBotSessionRunner"/> доводит сессию до конца, на <see cref="ScriptedLlmClient"/>, без единого обращения к реальной LLM.</summary>
public sealed class LlmBotSessionRunnerTests
{
    [Fact]
    public async Task RunToCompletionAsync_DrivesSessionToFinish()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession(endTurn: 3);
        // Один "nop" на каждый ход decision-фазы (ходы 1, 2, 3).
        var client = new ScriptedLlmClient("""{"kind":"nop"}""", """{"kind":"nop"}""", """{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);
        var log = new BotDecisionLog();
        var completedTurns = new List<int>();

        var result = await LlmBotSessionRunner.RunToCompletionAsync(
            session, [bot], new Random(1), log, onTurnCompleted: s => completedTurns.Add(s.State.CurrentTurn));

        Assert.True(session.State.IsFinished);
        Assert.Equal(LlmBotSessionStopReason.Completed, result.Reason);
        Assert.Equal(3, log.Entries.Count);
        Assert.Equal([1, 2, 3], completedTurns);
    }

    [Fact]
    public async Task RunToCompletionAsync_ConsecutiveExhaustedTurns_StopsEarlyInsteadOfGrindingToTheEnd()
    {
        // Живой прогон стадии 1, 2026-08-16: сессия честно доехала до конца, но 9 из 12 ходов подряд
        // бот не смог получить ни одного валидного ответа — "нет смысла гнать ходы до конца".
        var (session, teamId) = TestSession.StartSingleTeamSession(endTurn: 10);
        var client = new ScriptedLlmClient("not json", "not json", "not json"); // 3 попытки на ход, все мимо
        var bot = new LlmBot(teamId, "persona", client, maxAttempts: 1);
        var log = new BotDecisionLog();
        var completedTurns = new List<int>();

        var result = await LlmBotSessionRunner.RunToCompletionAsync(
            session, [bot], new Random(1), log, maxConsecutiveExhaustedTurns: 3,
            onTurnCompleted: s => completedTurns.Add(s.State.CurrentTurn));

        Assert.False(session.State.IsFinished);
        Assert.Equal(LlmBotSessionStopReason.RepeatedFailures, result.Reason);
        Assert.Contains(teamId.ToString(), result.Detail);
        Assert.Equal([1, 2], completedTurns); // остановился на 3-м подряд провале, не дойдя до 10-го хода
    }

    [Fact]
    public async Task RunToCompletionAsync_ExhaustedThenSuccess_ResetsStreakAndKeepsGoing()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession(endTurn: 3);
        var client = new ScriptedLlmClient(
            "not json", // ход 1: одна попытка, мимо — Exhausted
            """{"kind":"nop"}""", // ход 2: успех — сбрасывает счётчик
            "not json"); // ход 3: снова Exhausted, но серия только 1, до порога 2 не хватает
        var bot = new LlmBot(teamId, "persona", client, maxAttempts: 1);
        var log = new BotDecisionLog();

        var result = await LlmBotSessionRunner.RunToCompletionAsync(
            session, [bot], new Random(1), log, maxConsecutiveExhaustedTurns: 2);

        Assert.True(session.State.IsFinished);
        Assert.Equal(LlmBotSessionStopReason.Completed, result.Reason);
    }

    [Fact]
    public async Task RunToCompletionAsync_WithMetricsLog_RecordsOneRowPerTurn()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession(endTurn: 2);
        var client = new ScriptedLlmClient("""{"kind":"nop"}""", """{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);
        var writer = new StringWriter();
        using var metrics = new BotMetricsLog(writer);

        await LlmBotSessionRunner.RunToCompletionAsync(session, [bot], new Random(1), new BotDecisionLog(), metrics);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(3, lines.Length); // header + 2 rows
    }

    [Fact]
    public async Task RunToCompletionAsync_OnStatusLine_ReportsBeforeAndAfterEachBotsTurn()
    {
        // Запрос пользователя 2026-08-16: живой построчный статус для автономного прогона без
        // консоли под рукой — "бот 2, ход 14, баланс такой то, запущен запрос к llm во столько то".
        var (session, teamId) = TestSession.StartSingleTeamSession(endTurn: 1);
        var client = new ScriptedLlmClient("""{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);
        var lines = new List<string>();

        await LlmBotSessionRunner.RunToCompletionAsync(
            session, [bot], new Random(1), new BotDecisionLog(), onStatusLine: lines.Add);

        Assert.Equal(2, lines.Count);
        Assert.Contains("Команда: ход 1 — запрос к LLM...", lines[0]);
        Assert.Contains("Команда: ход 1 — Nop", lines[1]);
        Assert.Contains("nop", lines[1]);
    }
}
