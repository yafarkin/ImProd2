namespace Game.Bots.Llm.Tests;

/// <summary>
/// Доказывает цикл execute→validate→retry (шаг 1 плана LLM-ботов, docs/TODO.md #20) на реальной
/// сессии, но без единого обращения к настоящей LLM — <see cref="ScriptedLlmClient"/> отдаёт заранее
/// прописанные ответы.
/// </summary>
public sealed class LlmBotDecisionLoopTests
{
    private static LlmBotDecisionLoop CreateLoop(ScriptedLlmClient client, int maxAttempts = 3) =>
        new(client, new BotCommandExecutor(), maxAttempts);

    [Fact]
    public async Task ValidCommandOnFirstAttempt_ExecutesAndSucceeds()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var result = await loop.RunTurnAsync(session, teamId, "system", "user", log);

        Assert.Equal(LlmBotTurnOutcome.Success, result.Outcome);
        Assert.Equal(1, result.Attempts);
        Assert.Single(session.State.Teams[teamId].Factories);
        Assert.Single(log.Entries);
        Assert.Equal("Success", log.Entries[0].Outcome);
    }

    [Fact]
    public async Task DomainErrorThenValid_RetriesAndSucceedsWithErrorTextInNextPrompt()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"unknown-factory"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var result = await loop.RunTurnAsync(session, teamId, "system", "user", log);

        Assert.Equal(LlmBotTurnOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Single(session.State.Teams[teamId].Factories);

        Assert.Equal(2, log.Entries.Count);
        Assert.Contains("Domain error", log.Entries[0].Outcome);
        Assert.Equal("Success", log.Entries[1].Outcome);

        Assert.Equal(2, client.ReceivedUserPrompts.Count);
        Assert.Contains("Unknown factory definition", client.ReceivedUserPrompts[1]);
    }

    [Fact]
    public async Task AllAttemptsInvalid_GivesUpWithoutThrowingAndLeavesStateUnchanged()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"unknown-1"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"unknown-2"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"unknown-3"}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client, maxAttempts: 3);

        var result = await loop.RunTurnAsync(session, teamId, "system", "user", log);

        Assert.Equal(LlmBotTurnOutcome.Exhausted, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Empty(session.State.Teams[teamId].Factories);

        Assert.Equal(4, log.Entries.Count);
        Assert.All(log.Entries.Take(3), e => Assert.Contains("Domain error", e.Outcome));
        Assert.Equal("Exhausted", log.Entries[3].Outcome);
    }

    [Fact]
    public async Task MalformedJson_TreatedAsRetryableErrorNotException()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            "not json at all",
            """{"kind":"nop"}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var result = await loop.RunTurnAsync(session, teamId, "system", "user", log);

        Assert.Equal(LlmBotTurnOutcome.Nop, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Contains("Parse error", log.Entries[0].Outcome);
    }

    [Fact]
    public async Task MissingRequiredKindField_TreatedAsRetryableErrorNotException()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("{}", """{"kind":"nop"}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var result = await loop.RunTurnAsync(session, teamId, "system", "user", log);

        Assert.Equal(LlmBotTurnOutcome.Nop, result.Outcome);
        Assert.Contains("Parse error", log.Entries[0].Outcome);
    }

    [Fact]
    public async Task Nop_EndsTurnWithoutStateChange()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"kind":"nop"}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var result = await loop.RunTurnAsync(session, teamId, "system", "user", log);

        Assert.Equal(LlmBotTurnOutcome.Nop, result.Outcome);
        Assert.Equal(1, result.Attempts);
        Assert.Empty(session.State.Teams[teamId].Factories);
        Assert.Equal("Nop", log.Entries[0].Outcome);
    }
}
