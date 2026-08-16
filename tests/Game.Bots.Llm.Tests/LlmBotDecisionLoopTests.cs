namespace Game.Bots.Llm.Tests;

/// <summary>
/// Доказывает поведение одного вызова LLM на весь ход (запрос пользователя 2026-08-16: «только раз
/// за ход обращаться к LLM, и чтобы он сразу формировал массив команд на ход») на реальной сессии, но
/// без единого обращения к настоящей LLM — <see cref="ScriptedLlmClient"/> отдаёт заранее прописанные
/// ответы. Ответ — объект <c>{"actions": [...]}</c> (см. <see cref="BotCommandBatch"/>), а не одна
/// команда, как раньше.
/// </summary>
public sealed class LlmBotDecisionLoopTests
{
    private static LlmBotDecisionLoop CreateLoop(ScriptedLlmClient client, int maxAttempts = 3) =>
        new(client, new BotCommandExecutor(), maxAttempts);

    [Fact]
    public async Task ValidCommandOnFirstAttempt_ExecutesAndSucceeds()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Single(results);
        Assert.Equal(LlmBotTurnOutcome.Success, results[0].Outcome);
        Assert.Equal(1, results[0].Attempts);
        Assert.Single(session.State.Teams[teamId].Factories);
        // Запрос пользователя 2026-08-16: записи должны быть помечены, какому боту/ходу они
        // принадлежат, и хранить сам текст запроса — иначе многочасовой многоботовый лог
        // невозможно разобрать после факта.
        Assert.Equal("Команда", log.Entries[0].BotLabel);
        Assert.Equal(1, log.Entries[0].Turn);
        Assert.Equal("user", log.Entries[0].UserPrompt);
    }

    [Fact]
    public async Task DecisionLog_CreateFile_PersistsEveryAttemptImmediately()
    {
        // Запрос пользователя 2026-08-16: "если упадёт — всё что было наработано, останется на
        // диске" — проверяем это буквально, читая файл, пока лог ещё открыт (без Dispose), как
        // было бы после аварийного завершения процесса.
        var path = Path.Combine(Path.GetTempPath(), $"decisions-{Ulid.NewUlid()}.jsonl");
        try
        {
            var (session, teamId) = TestSession.StartSingleTeamSession();
            var client = new ScriptedLlmClient(
                "not json at all",
                """{"actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}]}""");
            using (var log = BotDecisionLog.CreateFile(path))
            {
                var loop = CreateLoop(client);
                await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

                // Ещё до Dispose — файл уже должен содержать все попытки: неудачный разбор, затем
                // успешный разбор второй попытки (отдельная строка) и итог самого действия.
                var linesWhileOpen = File.ReadAllLines(path);
                Assert.Equal(3, linesWhileOpen.Length);
                Assert.Contains("Parse error", linesWhileOpen[0]);
                Assert.Contains("Parsed 1 action(s)", linesWhileOpen[1]);
                Assert.Contains("Success", linesWhileOpen[2]);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BatchWithDomainErrorAndValidAction_SkipsInvalidExecutesValid()
    {
        // Ключевое отличие от прежней версии (один вызов LLM на одно действие): доменная ошибка
        // ОДНОГО действия внутри уже распарсенного массива больше не запускает повторный запрос к
        // модели с текстом ошибки — она просто пропускается, а остальные действия того же массива
        // всё равно исполняются (см. doc-comment LlmBotDecisionLoop).
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"buildFactory","factoryDefinitionId":"unknown-factory"},{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Equal(2, results.Count);
        Assert.Equal(LlmBotTurnOutcome.Skipped, results[0].Outcome);
        Assert.Contains("Unknown factory definition", results[0].SkipReason);
        Assert.Equal(LlmBotTurnOutcome.Success, results[1].Outcome);
        Assert.Single(session.State.Teams[teamId].Factories); // только валидная команда реально построила фабрику

        Assert.Contains(log.Entries, e => e.Outcome.Contains("domain error"));
        Assert.Contains(log.Entries, e => e.Outcome == "Success");
    }

    [Fact]
    public async Task AllAttemptsUnparsableJson_GivesUpWithoutThrowingAndLeavesStateUnchanged()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("not json 1", "not json 2", "not json 3");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client, maxAttempts: 3);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Single(results);
        Assert.Equal(LlmBotTurnOutcome.Exhausted, results[0].Outcome);
        Assert.Equal(3, results[0].Attempts);
        Assert.Empty(session.State.Teams[teamId].Factories);

        Assert.Equal(4, log.Entries.Count);
        Assert.All(log.Entries.Take(3), e => Assert.Contains("Parse error", e.Outcome));
        Assert.Equal("Exhausted", log.Entries[3].Outcome);
    }

    [Fact]
    public async Task MalformedJson_TreatedAsRetryableErrorNotException()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("not json at all", """{"actions":[]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Single(results);
        Assert.Equal(LlmBotTurnOutcome.Nop, results[0].Outcome);
        Assert.Contains("Parse error", log.Entries[0].Outcome);
    }

    [Fact]
    public async Task MissingRequiredActionsField_TreatedAsRetryableErrorNotException()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("{}", """{"actions":[]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Single(results);
        Assert.Equal(LlmBotTurnOutcome.Nop, results[0].Outcome);
        Assert.Contains("Parse error", log.Entries[0].Outcome);
    }

    [Fact]
    public async Task EmptyActionsArray_ReturnsSingleNopResult()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Single(results);
        Assert.Equal(LlmBotTurnOutcome.Nop, results[0].Outcome);
        Assert.Empty(session.State.Teams[teamId].Factories);
        Assert.Equal("Parsed 0 action(s)", log.Entries[0].Outcome);
    }

    [Fact]
    public async Task ExplicitNopItem_KeepsAnnotationSoItSurvivesIntoHistory()
    {
        // Живой прогон 2026-08-16 (прежняя версия) показал: без сохранения аннотации на nop-ответе
        // терялось единственное объяснение модели, почему она решила ничего не делать. В batch-режиме
        // тот же эффект достигается явным nop-элементом массива вместо пустого массива, когда модель
        // хочет оставить себе причину.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient("""{"actions":[{"kind":"nop","annotation":"waiting for a loan first"}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Single(results);
        Assert.Equal(LlmBotTurnOutcome.Nop, results[0].Outcome);
        Assert.NotNull(results[0].Command);
        Assert.Equal("waiting for a loan first", results[0].Command!.Annotation);
    }

    [Fact]
    public async Task ClientException_TreatedAsRetryableErrorNotFatal()
    {
        // Живая проверка 2026-08-16 (реальный конфиг стадии 1): промпт перерос контекст-окно
        // модели, LM Studio вернула HTTP 400 — LmStudioClient оборачивает это в
        // InvalidOperationException. Раньше это падало необработанным исключением и обрушивало
        // весь многочасовой прогон; теперь тратит попытку, как ошибка разбора.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient();
        client.EnqueueException(new InvalidOperationException("LM Studio request failed with 400 Bad Request: context length exceeded"));
        client.EnqueueException(new HttpRequestException("connection reset"));
        client.EnqueueException(new InvalidOperationException("still failing"));
        var log = new BotDecisionLog();
        var loop = CreateLoop(client, maxAttempts: 3);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Single(results);
        Assert.Equal(LlmBotTurnOutcome.Exhausted, results[0].Outcome);
        Assert.Equal(3, log.Entries.Count(e => e.Outcome.Contains("Client error")));
    }

    [Fact]
    public async Task ClientException_ThenValidResponse_Recovers()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient();
        client.EnqueueException(new InvalidOperationException("transient failure"));
        client.EnqueueResponse("""{"actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Single(results);
        Assert.Equal(LlmBotTurnOutcome.Success, results[0].Outcome);
        Assert.Contains("Client error", log.Entries[0].Outcome);
        Assert.Single(session.State.Teams[teamId].Factories);
    }

    [Fact]
    public async Task BatchWithDuplicateAction_SecondOccurrenceSkipped()
    {
        // Живая проверка 2026-08-16 (qwen3.8-27b без reasoning, TODO #20): модель слово в слово
        // повторяла emergencyPurchase с той же annotation несколько раз подряд в одном ходу.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"emergencyPurchase","materialId":"ore","volume":1000,"annotation":"buy ore"},{"kind":"emergencyPurchase","materialId":"ore","volume":1000,"annotation":"buy ore again"}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Equal(2, results.Count);
        Assert.Equal(LlmBotTurnOutcome.Success, results[0].Outcome);
        Assert.Equal(LlmBotTurnOutcome.Skipped, results[1].Outcome);
        Assert.Contains("identical", results[1].SkipReason);
    }

    [Fact]
    public async Task BatchWithDuplicateBuildFactory_BothSucceed()
    {
        // Несколько фабрик одного типа подряд — валидная стратегия масштабирования, не залипание
        // (см. doc-comment LlmBotDecisionLoop) — детектор повтора не должен её блокировать.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"buildFactory","factoryDefinitionId":"iron-mine"},{"kind":"buildFactory","factoryDefinitionId":"iron-mine"}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(LlmBotTurnOutcome.Success, r.Outcome));
        Assert.Equal(2, session.State.Teams[teamId].Factories.Count);
    }

    [Fact]
    public async Task BatchWithSecondEmergencyPurchaseOfSameMaterialDifferentVolume_Skipped()
    {
        // Живая проверка 2026-08-16 (после исправления буквального повтора): модель обходила его,
        // слегка меняя volume каждый раз (тот же материал, тот же ход) — см. doc-comment
        // LlmBotDecisionLoop, пункт 2. Разный объём — уже не "буквальный повтор", но домен-правило
        // (LIMIT в SystemPromptBuilder) режет и это.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"emergencyPurchase","materialId":"ore","volume":1000},{"kind":"emergencyPurchase","materialId":"ore","volume":50}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Equal(2, results.Count);
        Assert.Equal(LlmBotTurnOutcome.Success, results[0].Outcome);
        Assert.Equal(LlmBotTurnOutcome.Skipped, results[1].Outcome);
        Assert.Contains("second emergencyPurchase", results[1].SkipReason);
    }

    [Fact]
    public async Task BatchWithEmergencyPurchaseOfDifferentMaterials_BothSucceed()
    {
        // Две разные нехватки в одном ходу — легитимно, лимит считается по materialId, не по kind в целом.
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"emergencyPurchase","materialId":"ore","volume":100},{"kind":"emergencyPurchase","materialId":"sheet","volume":50}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 5);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(LlmBotTurnOutcome.Success, r.Outcome));
    }

    [Fact]
    public async Task BatchLongerThanCap_ExtraActionsTruncated()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var client = new ScriptedLlmClient(
            """{"actions":[{"kind":"takeLoan","amount":1},{"kind":"takeLoan","amount":2},{"kind":"takeLoan","amount":3}]}""");
        var log = new BotDecisionLog();
        var loop = CreateLoop(client);

        var results = await loop.RunTurnAsync(session, teamId, "system", "user", log, maxActionsPerTurn: 2);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(LlmBotTurnOutcome.Success, r.Outcome));
    }
}
