using System.Text.Json;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>Итог одного действия в ходе одной команды под управлением LLM-бота.</summary>
public enum LlmBotTurnOutcome
{
    /// <summary>Команда успешно исполнена.</summary>
    Success,

    /// <summary>
    /// Бот явно попросил ничего не делать (или прислал пустой <see cref="BotCommandBatch.Actions"/>
    /// — запрос пользователя 2026-08-16: один вызов LLM на весь ход, пустой массив действий — это и
    /// есть «нечего делать», отдельный kind=nop-ответ на своём вызове больше не нужен).
    /// </summary>
    Nop,

    /// <summary>
    /// Команда была синтаксически валидна, но не исполнена — либо доменная ошибка (неизвестный id,
    /// нарушение правила движка), либо её отклонил анти-залипательный guard (буквальный повтор,
    /// вторая emergencyPurchase того же материала за ход, см. <see cref="LlmBotDecisionLoop"/>). В
    /// отличие от прежней версии (один вызов LLM на одно действие) здесь НЕТ повторного запроса к
    /// модели с текстом ошибки — весь ход решается одним вызовом, так что отклонённое действие просто
    /// пропускается, а остальные действия из того же массива всё равно исполняются.
    /// </summary>
    Skipped,

    /// <summary>Потолок попыток исчерпан — ни одна не дала распарсиваемый <see cref="BotCommandBatch"/>.</summary>
    Exhausted,
}

/// <summary>Результат одного действия — элемент <see cref="LlmBotTurnReport.Actions"/>.</summary>
public sealed record LlmBotTurnResult(LlmBotTurnOutcome Outcome, int Attempts, BotCommand? Command, string? SkipReason = null)
{
    public static LlmBotTurnResult ForSuccess(int attempts, BotCommand command) => new(LlmBotTurnOutcome.Success, attempts, command);

    /// <summary>
    /// <paramref name="command"/> сохраняется (не отбрасывается в <see langword="null"/>), хотя ход и
    /// пустой, — иначе теряется <see cref="BotCommand.Annotation"/>, а с ней и объяснение модели,
    /// почему она решила ничего не делать (ровно то, что аннотации должны сохранять).
    /// </summary>
    public static LlmBotTurnResult ForNop(int attempts, BotCommand command) => new(LlmBotTurnOutcome.Nop, attempts, command);

    /// <summary>
    /// <paramref name="reason"/> уходит и в <see cref="BotDecisionLog"/>, и (через
    /// <see cref="BotCommandSummary.Describe"/>) в кросс-ходовую историю бота — единственный способ
    /// модели узнать в будущем ходу, что именно не сработало, раз внутриходовой ретрай с исправлением
    /// теперь не делается (см. doc-comment <see cref="LlmBotTurnOutcome.Skipped"/>).
    /// </summary>
    public static LlmBotTurnResult ForSkipped(int attempts, BotCommand command, string reason) => new(LlmBotTurnOutcome.Skipped, attempts, command, reason);

    public static LlmBotTurnResult ForExhausted(int attempts) => new(LlmBotTurnOutcome.Exhausted, attempts, null);
}

/// <summary>
/// Один вызов <see cref="ILlmClient"/> на весь ход одной команды (запрос пользователя 2026-08-16:
/// «только раз за ход обращаться к LLM, и чтобы он сразу формировал массив команд на ход» — до этого
/// каждое действие хода было отдельным вызовом с собственным ретраем/поправкой; история этого решения
/// и его недостатков — см. <c>docs/TODO.md #20</c>). Разбирает ответ как <see cref="BotCommandBatch"/>
/// и исполняет по порядку каждый элемент через <see cref="BotCommandExecutor"/>.
/// <para>
/// Ретрай (<paramref name="maxAttempts"/> в конструкторе) — только на уровне ВСЕГО вызова: битый JSON
/// или сетевая/HTTP-ошибка добавляют текст ошибки к промпту и повторяют весь запрос (тот же приём,
/// что был и раньше). Доменная ошибка ОДНОГО действия внутри уже распарсенного массива, наоборот,
/// больше НЕ вызывает повторный запрос к модели — она просто пропускается
/// (<see cref="LlmBotTurnOutcome.Skipped"/>), а остальные действия массива всё равно исполняются:
/// повторный запрос ради исправления одной команды стоил бы ещё один вызов LLM, что и убирает весь
/// смысл «одного вызова на ход».
/// </para>
/// <para>
/// Два guard'а против залипания (оба — живые находки 2026-08-16, TODO #20), применяются по ходу
/// исполнения массива, сравнивая каждое следующее действие с уже принятыми в ЭТОМ ЖЕ массиве:
/// </para>
/// <list type="number">
/// <item><description>
/// Буквальный повтор — модель (qwen3.8-27b без reasoning) слово в слово повторяла
/// <c>emergencyPurchase(coking-coal, 1000)</c> с той же annotation по 6-7 раз подряд. Если
/// предложенное действие (без учёта <see cref="BotCommand.Annotation"/>) буквально совпадает с любым
/// уже принятым в этом массиве — оно пропускается. Исключение — <see cref="BotCommandKind.BuildFactory"/>:
/// несколько фабрик одного типа подряд — валидная стратегия масштабирования, не залипание.
/// </description></item>
/// <item><description>
/// Первую проверку легко обойти, слегка варьируя число: следующая живая проверка показала ту же
/// модель, штампующую <c>emergencyPurchase</c> одного материала по 4-5 раз за ход, каждый раз с чуть
/// другим объёмом. <see cref="BotCommandKind.EmergencyPurchase"/> описан в системном промпте как «для
/// разового форс-мажора» — второе действие этого вида для ОДНОГО материала за ход отклоняется
/// независимо от объёма.
/// </description></item>
/// </list>
/// <para>
/// Особый случай для <see cref="BotCommandKind.SetWorkerCount"/> и других команд, нацеленных на
/// конкретную фабрику по <see cref="BotCommand.FactoryId"/>: если в этом же массиве раньше стоит
/// <see cref="BotCommandKind.BuildFactory"/> для только что построенной фабрики, модель физически не
/// может знать её реальный <see cref="Ulid"/> заранее (движок генерирует его в момент исполнения) —
/// значит, "построить и тут же нанять рабочих на неё" за один вызов невозможно в принципе, только на
/// следующий ход, когда фабрика уже появится в снимке состояния со своим id. Явно проговорено в
/// <see cref="SystemPromptBuilder"/>, не багфикс, осознанное ограничение батч-режима.
/// </para>
/// </summary>
public sealed class LlmBotDecisionLoop
{
    private readonly ILlmClient _client;
    private readonly BotCommandExecutor _executor;
    private readonly int _maxAttempts;

    public LlmBotDecisionLoop(ILlmClient client, BotCommandExecutor executor, int maxAttempts = 3)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(executor);
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Must allow at least one attempt.");
        }

        _client = client;
        _executor = executor;
        _maxAttempts = maxAttempts;
    }

    /// <summary>
    /// Прогоняет весь ход одной команды одним вызовом <see cref="ILlmClient"/> (см. doc-comment
    /// класса). <paramref name="maxActionsPerTurn"/> — жёсткий потолок длины массива действий:
    /// избыток молча отбрасывается (тот же приём, что раньше страховал потолок вызовов, теперь —
    /// потолок длины одного ответа). Каждый запрос — самостоятельный, без накопленного контекста
    /// (решение пользователя: не вести переписку) — на повторной попытке к
    /// <paramref name="initialUserPrompt"/> дописывается текст последней ошибки.
    /// </summary>
    public async Task<IReadOnlyList<LlmBotTurnResult>> RunTurnAsync(
        GameSession session,
        Ulid teamId,
        string systemPrompt,
        string initialUserPrompt,
        BotDecisionLog log,
        int maxActionsPerTurn,
        Random random,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(initialUserPrompt);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(random);

        var botLabel = session.State.Teams.TryGetValue(teamId, out var team) ? team.Name : teamId.ToString();
        var turn = session.State.CurrentTurn;
        var userPrompt = initialUserPrompt;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            string raw;
            try
            {
                raw = await _client.CompleteAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Живая проверка 2026-08-16 (реальный конфиг стадии 1, промпт перерос контекст-окно
                // модели): LM Studio вернула HTTP 400, и это обрушило весь многочасовой прогон —
                // один ход одного бота не должен ронять всю сессию. Не удлиняем userPrompt текстом
                // ошибки, как при разборе — тут ошибка не в содержимом ответа модели, добавлять к
                // промпту нечего.
                log.Record(botLabel, turn, 0, attempt, userPrompt, string.Empty, $"Client error: {ex.Message}");
                continue;
            }

            var (batch, parseError) = TryParse(raw);

            if (parseError is not null)
            {
                log.Record(botLabel, turn, 0, attempt, userPrompt, raw, $"Parse error: {parseError}");
                userPrompt = WithError(initialUserPrompt, parseError);
                continue;
            }

            log.Record(botLabel, turn, 0, attempt, userPrompt, raw, $"Parsed {batch!.Actions.Count} action(s)");
            return ExecuteBatch(batch, session, teamId, log, botLabel, turn, attempt, maxActionsPerTurn, random);
        }

        log.Record(botLabel, turn, 0, _maxAttempts, userPrompt, string.Empty, "Exhausted");
        return [LlmBotTurnResult.ForExhausted(_maxAttempts)];
    }

    private IReadOnlyList<LlmBotTurnResult> ExecuteBatch(
        BotCommandBatch batch, GameSession session, Ulid teamId, BotDecisionLog log, string botLabel, int turn, int attempt, int maxActionsPerTurn, Random random)
    {
        if (batch.Actions.Count == 0)
        {
            var nopCommand = new BotCommand { Kind = BotCommandKind.Nop };
            return [LlmBotTurnResult.ForNop(attempt, nopCommand)];
        }

        var actions = batch.Actions.Count > maxActionsPerTurn ? batch.Actions.Take(maxActionsPerTurn).ToList() : batch.Actions;
        var results = new List<LlmBotTurnResult>(actions.Count);
        var executedCommands = new List<BotCommand>(actions.Count);

        for (var i = 0; i < actions.Count; i++)
        {
            var command = actions[i];
            var actionIndex = i + 1;

            if (command.Kind == BotCommandKind.Nop)
            {
                log.Record(botLabel, turn, actionIndex, attempt, string.Empty, Describe(command), "Nop");
                results.Add(LlmBotTurnResult.ForNop(attempt, command));
                continue;
            }

            if (IsExactRepeatOfAnyPriorAction(command, executedCommands))
            {
                log.Record(botLabel, turn, actionIndex, attempt, string.Empty, Describe(command), "Skipped: identical to an earlier action this turn");
                results.Add(LlmBotTurnResult.ForSkipped(attempt, command, RepeatSkipReason));
                continue;
            }

            if (IsSecondEmergencyPurchaseOfSameMaterial(command, executedCommands))
            {
                log.Record(botLabel, turn, actionIndex, attempt, string.Empty, Describe(command), "Skipped: second emergencyPurchase of same material this turn");
                results.Add(LlmBotTurnResult.ForSkipped(attempt, command, EmergencyPurchaseCapSkipReason));
                continue;
            }

            var executionResult = _executor.Execute(command, session, teamId, random);
            if (executionResult is BotCommandExecutionResult.DomainError error)
            {
                log.Record(botLabel, turn, actionIndex, attempt, string.Empty, Describe(command), $"Skipped: domain error: {error.Message}");
                results.Add(LlmBotTurnResult.ForSkipped(attempt, command, error.Message));
                continue;
            }

            log.Record(botLabel, turn, actionIndex, attempt, string.Empty, Describe(command), "Success");
            results.Add(LlmBotTurnResult.ForSuccess(attempt, command));
            executedCommands.Add(command);
        }

        return results;
    }

    private static (BotCommandBatch? Batch, string? Error) TryParse(string raw)
    {
        try
        {
            var batch = JsonSerializer.Deserialize<BotCommandBatch>(raw, BotCommandSerialization.Options);
            return batch is null ? (null, "Response deserialized to null.") : (batch, null);
        }
        catch (JsonException ex)
        {
            return (null, ex.Message);
        }
    }

    private static string Describe(BotCommand command) => JsonSerializer.Serialize(command, BotCommandSerialization.Options);

    private static string WithError(string initialUserPrompt, string errorMessage) =>
        $"{initialUserPrompt}\n\nPrevious attempt failed: {errorMessage}\nRespond again with a corrected object matching the schema.";

    private const string RepeatSkipReason = "identical to an earlier action this turn — not executed";

    private const string EmergencyPurchaseCapSkipReason = "second emergencyPurchase of this material this turn — not executed";

    /// <summary>См. doc-comment класса (пункт 1) — buildFactory намеренно исключён, повтор с тем же типом/рецептом валиден.</summary>
    private static bool IsExactRepeatOfAnyPriorAction(BotCommand command, IReadOnlyList<BotCommand> priorCommands)
    {
        if (command.Kind == BotCommandKind.BuildFactory)
        {
            return false;
        }

        var normalized = command with { Annotation = null };
        return priorCommands.Any(prior => prior with { Annotation = null } == normalized);
    }

    /// <summary>См. doc-comment класса (пункт 2) — второй emergencyPurchase того же материала за ход, любой объём.</summary>
    private static bool IsSecondEmergencyPurchaseOfSameMaterial(BotCommand command, IReadOnlyList<BotCommand> priorCommands) =>
        command.Kind == BotCommandKind.EmergencyPurchase
        && priorCommands.Any(prior => prior.Kind == BotCommandKind.EmergencyPurchase && prior.MaterialId == command.MaterialId);
}
