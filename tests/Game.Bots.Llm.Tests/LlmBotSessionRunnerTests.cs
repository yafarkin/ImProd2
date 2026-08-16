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

        await LlmBotSessionRunner.RunToCompletionAsync(
            session, [bot], new Random(1), log, onTurnCompleted: s => completedTurns.Add(s.State.CurrentTurn));

        Assert.True(session.State.IsFinished);
        Assert.Equal(3, log.Entries.Count);
        Assert.Equal([1, 2, 3], completedTurns);
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
}
