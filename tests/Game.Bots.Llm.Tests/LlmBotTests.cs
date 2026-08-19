namespace Game.Bots.Llm.Tests;

/// <summary>
/// <see cref="LlmBot"/> целиком — сборка промпта, ровно один вызов LLM на весь ход, возвращающий
/// массив действий (запрос пользователя 2026-08-16: «только раз за ход обращаться к LLM, и чтобы он
/// сразу формировал массив команд на ход»), накопление собственной истории между ходами — на
/// <see cref="ScriptedLlmClient"/>, без единого обращения к реальной LLM.
/// </summary>
public sealed class LlmBotTests
{
    [Fact]
    public async Task TakeTurnAsync_BuildsPromptContainingStateAndEmptyHistory()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[]}""");
        var bot = new LlmBot(teamId, "Cautious and risk-averse.", client);

        await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Single(client.ReceivedUserPrompts); // ровно один вызов LLM на весь ход
        var userPrompt = client.ReceivedUserPrompts[0];
        Assert.Contains("YOUR TEAM (sector A)", userPrompt);
        Assert.Contains("=== HISTORY (sampled turns: 1) ===", userPrompt);
        Assert.Contains("this is your first turn", userPrompt);
    }

    [Fact]
    public async Task TakeTurnAsync_ConstructedWithInitialHistory_SeesItOnTheVeryFirstPrompt()
    {
        // Запрос пользователя 2026-08-19: после Ctrl+C и возобновления бот не должен просыпаться с
        // чистой памятью — initialHistory должно попасть в промпт уже на первом же вызове TakeTurnAsync
        // этого экземпляра, не только начиная со второго (как обычное накопление через Add).
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[]}""");
        var restoredHistory = new[]
        {
            new BotTurnHistoryEntry(3, [new BotTurnActionRecord("takeLoan(2000)", "capital before the crash")]),
        };
        var bot = new LlmBot(teamId, "persona", client, initialHistory: restoredHistory);

        await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Contains("Turn 3: takeLoan(2000) — capital before the crash", client.ReceivedUserPrompts[0]);
        // Восстановленная запись (ход 3) осталась в истории вместе со свежей (ход 1 — этот же вызов
        // TakeTurnAsync добавил её как обычно, поверх уже сидевшей initialHistory, не вместо неё).
        Assert.Equal(2, bot.History.Count);
        Assert.Contains(bot.History, entry => entry.Turn == 3);
    }

    [Fact]
    public async Task TakeTurnAsync_EmptyActionsArray_ReportHasSingleNopAction()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[]}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Single(report.Actions);
        Assert.Equal(LlmBotTurnOutcome.Nop, report.Actions[0].Outcome);
        Assert.True(report.EndedWithNop);
        Assert.Equal(0, report.SuccessfulActionCount);
        Assert.False(report.IsFullyFailedTurn);
    }

    [Fact]
    public async Task TakeTurnAsync_MultipleActionsInOneBatch_ExecutesAllInOrder()
    {
        // Прямой запрос пользователя 2026-08-16: "только раз за ход обращаться к LLM, и чтобы он
        // сразу формировал массив команд на ход" — весь план хода приходит одним ответом.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"takeLoan","amount":1000,"annotation":"bootstrap capital"},{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}]}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Single(client.ReceivedUserPrompts); // по-прежнему ровно один вызов
        Assert.Equal(2, report.Actions.Count);
        Assert.Equal(LlmBotTurnOutcome.Success, report.Actions[0].Outcome);
        Assert.Equal(LlmBotTurnOutcome.Success, report.Actions[1].Outcome);
        Assert.Equal(2, report.SuccessfulActionCount);
        Assert.Single(session.State.Teams[teamId].Factories);
    }

    [Fact]
    public async Task TakeTurnAsync_ActionCapReached_ExtraActionsTruncatedWithoutHangingOrThrowing()
    {
        // Запрос пользователя 2026-08-16: "надо убедиться, что модель умеет останавливаться" —
        // страховка на случай, если нет: потолок молча отбрасывает лишнее, не бросает исключение.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"setGenerationResearchCommitment","amount":1},{"kind":"setGenerationResearchCommitment","amount":2},{"kind":"setGenerationResearchCommitment","amount":3}]}""");
        var bot = new LlmBot(teamId, "persona", client, maxActionsPerTurn: 2);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Equal(2, report.Actions.Count);
        Assert.All(report.Actions, a => Assert.Equal(LlmBotTurnOutcome.Success, a.Outcome));
        Assert.Equal(2, report.SuccessfulActionCount);
    }

    [Fact]
    public async Task TakeTurnAsync_WithMetricsLog_RecordsOneRowPerAction()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}]}""");
        var bot = new LlmBot(teamId, "persona", client);
        var writer = new StringWriter();
        using var metrics = new BotMetricsLog(writer);

        await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1), metrics);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length); // header + 1 действие (buildFactory)
        var firstAction = lines[1].Split(',');
        Assert.Equal("Команда", firstAction[0]);
        Assert.Equal("1", firstAction[1]); // turn
        Assert.Equal("1", firstAction[2]); // action_index
        Assert.True(int.Parse(firstAction[3]) >= 0); // response_time_ms
        Assert.True(int.Parse(firstAction[4]) > 0); // request_size_bytes
        Assert.Equal("buildFactory(iron-mine)", firstAction[5]);
        Assert.Equal("1", firstAction[9]); // factory_count — BuildFactory мгновенный, виден сразу же
    }

    [Fact]
    public async Task TakeTurnAsync_WithoutMetricsLog_DoesNotThrow()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[]}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Equal(LlmBotTurnOutcome.Nop, report.Actions[^1].Outcome);
    }

    [Fact]
    public async Task TakeTurnAsync_RecordsAllActionsAndAnnotationsInHistory()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine","annotation":"starting the ore chain"}]}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Equal(LlmBotTurnOutcome.Success, report.Actions[0].Outcome);
        Assert.Single(bot.History);
        Assert.Equal(1, bot.History[0].Turn);
        Assert.Single(bot.History[0].Actions);
        Assert.Equal("buildFactory(iron-mine)", bot.History[0].Actions[0].Summary);
        Assert.Equal("starting the ore chain", bot.History[0].Actions[0].Annotation);
    }

    [Fact]
    public async Task TakeTurnAsync_ReasonIsAccepted_ButDoesNotLeakIntoHistory()
    {
        // Запрос пользователя 2026-08-19: "reason" объясняет действие здесь и сейчас, для разбора
        // человеком (см. BotDecisionLog), а не для памяти бота — в отличие от annotation, оно не
        // должно накапливаться в промпте из хода в ход.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine","reason":"ore is cheap and we have zero factories","annotation":"starting the ore chain"}]}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Equal(LlmBotTurnOutcome.Success, report.Actions[0].Outcome);
        Assert.Equal("ore is cheap and we have zero factories", report.Actions[0].Command!.Reason);
        Assert.Equal("starting the ore chain", bot.History[0].Actions[0].Annotation);
        Assert.DoesNotContain("ore is cheap", bot.History[0].Actions[0].Summary);
        Assert.DoesNotContain("ore is cheap", bot.History[0].Actions[0].Annotation);
    }

    [Fact]
    public async Task TakeTurnAsync_SecondTurn_SeesFirstTurnInPrompt()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine","annotation":"starting the ore chain"}]}""",
            """{"actions":[{"kind":"buildFactory","factoryDefinitionId":"steel-mill","annotation":"next in the chain"}]}""");
        var bot = new LlmBot(teamId, "persona", client);

        await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));
        await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Equal(2, bot.History.Count);
        Assert.Equal(2, client.ReceivedUserPrompts.Count); // всё ещё ровно один вызов на ход
        var firstPromptOfSecondTurn = client.ReceivedUserPrompts[1];
        Assert.Contains("Turn 1: buildFactory(iron-mine) — starting the ore chain", firstPromptOfSecondTurn);
    }

    [Fact]
    public async Task TakeTurnAsync_UnparsableResponse_IsFullyFailedTurn()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("not json 1", "not json 2");
        var bot = new LlmBot(teamId, "persona", client, maxAttempts: 2);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Equal(LlmBotTurnOutcome.Exhausted, report.Actions[^1].Outcome);
        Assert.True(report.IsFullyFailedTurn);
        Assert.Contains("retries exhausted", bot.History[0].Actions[^1].Summary);
    }

    [Fact]
    public async Task TakeTurnAsync_OneSuccessOneSkipped_IsNotFullyFailedTurn()
    {
        // Бот, взявший заём и предложивший вторым действием невалидную команду, всё же продвинулся —
        // не должен считаться полностью провальным ходом для circuit breaker в LlmBotSessionRunner
        // (запрос пользователя 2026-08-16: доменная ошибка одного действия в батче больше не рушит
        // весь ход, просто пропускается — см. doc-comment LlmBotDecisionLoop).
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"takeLoan","amount":1000},{"kind":"buildFactory","factoryDefinitionId":"unknown"}]}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Equal(1, report.SuccessfulActionCount);
        Assert.Equal(LlmBotTurnOutcome.Skipped, report.Actions[^1].Outcome);
        Assert.False(report.IsFullyFailedTurn);
    }

    [Fact]
    public async Task TakeTurnAsync_AllActionsSkipped_IsFullyFailedTurn()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"buildFactory","factoryDefinitionId":"unknown-1"},{"kind":"buildFactory","factoryDefinitionId":"unknown-2"}]}""");
        var bot = new LlmBot(teamId, "persona", client);

        var report = await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1));

        Assert.Equal(0, report.SuccessfulActionCount);
        Assert.All(report.Actions, a => Assert.Equal(LlmBotTurnOutcome.Skipped, a.Outcome));
        Assert.True(report.IsFullyFailedTurn);
    }

    [Fact]
    public async Task TakeTurnAsync_OnStatusLine_ReportsRequestThenEachAction()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[{"kind":"takeLoan","amount":1000}]}""");
        var bot = new LlmBot(teamId, "persona", client);
        var lines = new List<string>();

        await bot.TakeTurnAsync(session, new BotDecisionLog(), new Random(1), onStatusLine: lines.Add);

        Assert.Equal(2, lines.Count); // запрос к LLM (один на весь ход) + итог единственного действия
        Assert.Contains("запрос к LLM", lines[0]);
        Assert.Contains("действие 1/1", lines[1]);
    }
}
