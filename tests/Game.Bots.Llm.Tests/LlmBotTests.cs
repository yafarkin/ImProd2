namespace Game.Bots.Llm.Tests;

/// <summary>
/// <see cref="LlmBot"/> целиком — сборка промптов, цикл ретрая, накопление собственной истории между
/// ходами — на <see cref="ScriptedLlmClient"/>, без единого обращения к реальной LLM.
/// </summary>
public sealed class LlmBotTests
{
    [Fact]
    public async Task TakeTurnAsync_BuildsPromptContainingStateAndEmptyHistory()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "Cautious and risk-averse.", client);

        await bot.TakeTurnAsync(session, new BotDecisionLog());

        var userPrompt = client.ReceivedUserPrompts[0];
        Assert.Contains("YOUR TEAM (sector A)", userPrompt);
        Assert.Contains("=== HISTORY (sampled turns: 1) ===", userPrompt);
        Assert.Contains("this is your first turn", userPrompt);
    }

    [Fact]
    public async Task TakeTurnAsync_RecordsOutcomeAndAnnotationInHistory()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"iron-mine","annotation":"starting the ore chain"}""");
        var bot = new LlmBot(teamId, "persona", client);

        var result = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(LlmBotTurnOutcome.Success, result.Outcome);
        Assert.Single(bot.History);
        Assert.Equal(1, bot.History[0].Turn);
        Assert.Equal("buildFactory(iron-mine)", bot.History[0].Summary);
        Assert.Equal("starting the ore chain", bot.History[0].Annotation);
    }

    [Fact]
    public async Task TakeTurnAsync_SecondCall_SeesFirstTurnInPrompt()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"iron-mine","annotation":"starting the ore chain"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"steel-mill","annotation":"next in the chain"}""");
        var bot = new LlmBot(teamId, "persona", client);

        await bot.TakeTurnAsync(session, new BotDecisionLog());
        await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(2, bot.History.Count);
        var secondPrompt = client.ReceivedUserPrompts[1];
        Assert.Contains("Turn 1: buildFactory(iron-mine) — starting the ore chain", secondPrompt);
    }

    [Fact]
    public async Task TakeTurnAsync_ExhaustedOutcome_RecordsFailureSummary()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"unknown-1"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"unknown-2"}""");
        var bot = new LlmBot(teamId, "persona", client, maxAttempts: 2);

        var result = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(LlmBotTurnOutcome.Exhausted, result.Outcome);
        Assert.Contains("exhausted", bot.History[0].Summary);
    }
}
