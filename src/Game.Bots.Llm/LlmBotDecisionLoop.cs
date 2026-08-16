using System.Text.Json;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>Итог одного хода одной команды под управлением LLM-бота.</summary>
public enum LlmBotTurnOutcome
{
    /// <summary>Команда успешно исполнена не позже <see cref="LlmBotTurnResult.Attempts"/>-й попытки.</summary>
    Success,

    /// <summary>Бот явно попросил ничего не делать.</summary>
    Nop,

    /// <summary>Потолок попыток исчерпан — ни одна из них не прошла ни разбор, ни валидацию.</summary>
    Exhausted,
}

/// <summary>Результат <see cref="LlmBotDecisionLoop.RunTurnAsync"/>.</summary>
public sealed record LlmBotTurnResult(LlmBotTurnOutcome Outcome, int Attempts, BotCommand? Command)
{
    public static LlmBotTurnResult ForSuccess(int attempts, BotCommand command) => new(LlmBotTurnOutcome.Success, attempts, command);

    public static LlmBotTurnResult ForNop(int attempts) => new(LlmBotTurnOutcome.Nop, attempts, null);

    public static LlmBotTurnResult ForExhausted(int attempts) => new(LlmBotTurnOutcome.Exhausted, attempts, null);
}

/// <summary>
/// Цикл execute→validate→retry для одного хода одной команды (шаг 1 плана LLM-ботов, docs/TODO.md
/// #20): вызывает <see cref="ILlmClient"/>, разбирает и исполняет ответ через
/// <see cref="BotCommandExecutor"/>, при доменной ошибке или битом JSON добавляет текст ошибки к
/// промпту и повторяет — с потолком попыток, чтобы один галлюцинированный ответ не подвесил весь
/// прогон (риск №3 из обсуждения TODO #20). Промпт здесь — заглушка: настоящий снапшот состояния и
/// свёртка истории под контекст-окно — отдельный, более поздний шаг плана (шаг 4).
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
    /// Прогоняет цикл на один ход одной команды. Каждый запрос к <see cref="ILlmClient"/> —
    /// самостоятельный, без накопленного контекста (решение пользователя: не вести переписку) — на
    /// повторной попытке к <paramref name="initialUserPrompt"/> дописывается текст последней ошибки.
    /// </summary>
    public async Task<LlmBotTurnResult> RunTurnAsync(
        GameSession session,
        Ulid teamId,
        string systemPrompt,
        string initialUserPrompt,
        BotDecisionLog log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(initialUserPrompt);
        ArgumentNullException.ThrowIfNull(log);

        var userPrompt = initialUserPrompt;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            var raw = await _client.CompleteAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
            var (command, parseError) = TryParse(raw);

            if (parseError is not null)
            {
                log.Record(attempt, raw, $"Parse error: {parseError}");
                userPrompt = WithError(initialUserPrompt, parseError);
                continue;
            }

            if (command!.Kind == BotCommandKind.Nop)
            {
                log.Record(attempt, raw, "Nop");
                return LlmBotTurnResult.ForNop(attempt);
            }

            var result = _executor.Execute(command, session, teamId);
            if (result is BotCommandExecutionResult.DomainError error)
            {
                log.Record(attempt, raw, $"Domain error: {error.Message}");
                userPrompt = WithError(initialUserPrompt, error.Message);
                continue;
            }

            log.Record(attempt, raw, "Success");
            return LlmBotTurnResult.ForSuccess(attempt, command);
        }

        log.Record(_maxAttempts, string.Empty, "Exhausted");
        return LlmBotTurnResult.ForExhausted(_maxAttempts);
    }

    private static (BotCommand? Command, string? Error) TryParse(string raw)
    {
        try
        {
            var command = JsonSerializer.Deserialize<BotCommand>(raw, BotCommandSerialization.Options);
            return command is null ? (null, "Response deserialized to null.") : (command, null);
        }
        catch (JsonException ex)
        {
            return (null, ex.Message);
        }
    }

    private static string WithError(string initialUserPrompt, string errorMessage) =>
        $"{initialUserPrompt}\n\nPrevious attempt failed: {errorMessage}\nRespond again with a corrected command matching the schema.";
}
