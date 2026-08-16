namespace Game.Bots.Llm.Tests;

/// <summary>
/// <see cref="LlmBot"/> целиком — сборка промптов, цикл ретрая, несколько действий за один ход (запрос
/// пользователя 2026-08-16), накопление собственной истории между ходами — на
/// <see cref="ScriptedLlmClient"/>, без единого обращения к реальной LLM.
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
        Assert.Contains("=== THIS TURN (deciding action 1) ===", userPrompt);
        Assert.Contains("You have taken no actions yet this turn", userPrompt);
        Assert.Contains("this is your first turn", userPrompt);
    }

    [Fact]
    public async Task TakeTurnAsync_ImmediateNop_ReportHasSingleNopAction()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Single(report.Actions);
        Assert.Equal(LlmBotTurnOutcome.Nop, report.Actions[0].Outcome);
        Assert.True(report.EndedWithNop);
        Assert.Equal(0, report.SuccessfulActionCount);
        Assert.False(report.IsFullyFailedTurn);
    }

    [Fact]
    public async Task TakeTurnAsync_MultipleActionsInOneTurn_KeepsCallingUntilNop()
    {
        // Прямой запрос пользователя 2026-08-16: "важно уметь несколько команд за один ход" — бот
        // должен продолжать спрашивать LLM внутри одного хода, пока та сама не скажет nop.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"takeLoan","amount":1000,"annotation":"bootstrap capital"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}""",
            """{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(3, report.Actions.Count);
        Assert.Equal(LlmBotTurnOutcome.Success, report.Actions[0].Outcome);
        Assert.Equal(LlmBotTurnOutcome.Success, report.Actions[1].Outcome);
        Assert.Equal(LlmBotTurnOutcome.Nop, report.Actions[2].Outcome);
        Assert.Equal(2, report.SuccessfulActionCount);
        Assert.True(report.EndedWithNop);
        Assert.Single(session.State.Teams[teamId].Factories); // buildFactory реально исполнился, второй командой того же хода
    }

    [Fact]
    public async Task TakeTurnAsync_SecondAction_PromptListsFirstActionUnderThisTurn()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"takeLoan","amount":1000,"annotation":"bootstrap capital"}""",
            """{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);

        await bot.TakeTurnAsync(session, new BotDecisionLog());

        var secondActionPrompt = client.ReceivedUserPrompts[1];
        Assert.Contains("=== THIS TURN (deciding action 2) ===", secondActionPrompt);
        Assert.Contains("- Action 1: takeLoan(1000) — bootstrap capital", secondActionPrompt);
    }

    [Fact]
    public async Task TakeTurnAsync_ActionCapReached_StopsWithoutHangingOrThrowing()
    {
        // Запрос пользователя 2026-08-16: "надо убедиться, что модель умеет останавливаться" —
        // страховка на случай, если нет: потолок останавливает ход сам, не бросает исключение.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"setGenerationResearchCommitment","amount":1}""",
            """{"kind":"setGenerationResearchCommitment","amount":2}""",
            """{"kind":"setGenerationResearchCommitment","amount":3}""");
        var bot = new LlmBot(teamId, "persona", client, maxActionsPerTurn: 3);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(3, report.Actions.Count);
        Assert.All(report.Actions, a => Assert.Equal(LlmBotTurnOutcome.Success, a.Outcome));
        Assert.False(report.EndedWithNop); // остановились по потолку, не по nop
        Assert.Equal(3, report.SuccessfulActionCount);
    }

    [Fact]
    public async Task TakeTurnAsync_WithMetricsLog_RecordsOneRowPerAction()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}""",
            """{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);
        var writer = new StringWriter();
        using var metrics = new BotMetricsLog(writer);

        await bot.TakeTurnAsync(session, new BotDecisionLog(), metrics);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(3, lines.Length); // header + 2 действия (buildFactory, nop)
        var firstAction = lines[1].Split(',');
        Assert.Equal("Команда", firstAction[0]);
        Assert.Equal("1", firstAction[1]); // turn
        Assert.Equal("1", firstAction[2]); // action_index
        Assert.True(int.Parse(firstAction[3]) >= 0); // response_time_ms
        Assert.True(int.Parse(firstAction[4]) > 0); // request_size_bytes
        Assert.Equal("buildFactory(iron-mine)", firstAction[5]);
        Assert.Equal("1", firstAction[9]); // factory_count — BuildFactory мгновенный, виден сразу же

        var secondAction = lines[2].Split(',');
        Assert.Equal("2", secondAction[2]); // action_index
        Assert.Equal("nop", secondAction[5]);
    }

    [Fact]
    public async Task TakeTurnAsync_WithoutMetricsLog_DoesNotThrow()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(LlmBotTurnOutcome.Nop, report.Actions[^1].Outcome);
    }

    [Fact]
    public async Task TakeTurnAsync_RecordsAllActionsAndAnnotationsInHistory()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"iron-mine","annotation":"starting the ore chain"}""",
            """{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(LlmBotTurnOutcome.Success, report.Actions[0].Outcome);
        Assert.Single(bot.History);
        Assert.Equal(1, bot.History[0].Turn);
        Assert.Equal(2, bot.History[0].Actions.Count);
        Assert.Equal("buildFactory(iron-mine)", bot.History[0].Actions[0].Summary);
        Assert.Equal("starting the ore chain", bot.History[0].Actions[0].Annotation);
        Assert.Equal("nop", bot.History[0].Actions[1].Summary);
    }

    [Fact]
    public async Task TakeTurnAsync_SecondTurn_SeesFirstTurnInPrompt()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"iron-mine","annotation":"starting the ore chain"}""",
            """{"kind":"nop"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"steel-mill","annotation":"next in the chain"}""",
            """{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);

        await bot.TakeTurnAsync(session, new BotDecisionLog());
        await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(2, bot.History.Count);
        var firstPromptOfSecondTurn = client.ReceivedUserPrompts[2];
        Assert.Contains("Turn 1: buildFactory(iron-mine) — starting the ore chain; nop", firstPromptOfSecondTurn);
    }

    [Fact]
    public async Task TakeTurnAsync_ExhaustedOnFirstAction_IsFullyFailedTurn()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"buildFactory","factoryDefinitionId":"unknown-1"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"unknown-2"}""");
        var bot = new LlmBot(teamId, "persona", client, maxAttempts: 2);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(LlmBotTurnOutcome.Exhausted, report.Actions[^1].Outcome);
        Assert.True(report.IsFullyFailedTurn);
        Assert.Contains("exhausted", bot.History[0].Actions[^1].Summary);
    }

    [Fact]
    public async Task TakeTurnAsync_ExhaustedAfterOneSuccess_IsNotFullyFailedTurn()
    {
        // Бот, взявший заём и только потом застрявший на второй команде, всё же продвинулся — не
        // должен считаться полностью провальным ходом для circuit breaker в LlmBotSessionRunner.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"takeLoan","amount":1000}""",
            """{"kind":"buildFactory","factoryDefinitionId":"unknown"}""",
            """{"kind":"buildFactory","factoryDefinitionId":"still-unknown"}""");
        var bot = new LlmBot(teamId, "persona", client, maxAttempts: 2);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog());

        Assert.Equal(1, report.SuccessfulActionCount);
        Assert.Equal(LlmBotTurnOutcome.Exhausted, report.Actions[^1].Outcome);
        Assert.False(report.IsFullyFailedTurn);
    }

    [Fact]
    public async Task TakeTurnAsync_OnStatusLine_ReportsEachActionSeparately()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"kind":"takeLoan","amount":1000}""",
            """{"kind":"nop"}""");
        var bot = new LlmBot(teamId, "persona", client);
        var lines = new List<string>();

        await bot.TakeTurnAsync(session, new BotDecisionLog(), onStatusLine: lines.Add);

        Assert.Equal(4, lines.Count); // (запрос, итог) x 2 действия
        Assert.Contains("действие 1", lines[0]);
        Assert.Contains("действие 1", lines[1]);
        Assert.Contains("действие 2", lines[2]);
        Assert.Contains("действие 2", lines[3]);
    }
}
