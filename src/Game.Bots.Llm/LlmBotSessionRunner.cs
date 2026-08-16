using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>Почему <see cref="LlmBotSessionRunner.RunToCompletionAsync"/> остановился.</summary>
public enum LlmBotSessionStopReason
{
    /// <summary>Сессия дошла до конца обычным путём.</summary>
    Completed,

    /// <summary>Один бот подряд исчерпал попытки на нескольких ходах — гнать дальше бессмысленно.</summary>
    RepeatedFailures,
}

/// <summary>Итог прогона — <see cref="LlmBotSessionRunner.RunToCompletionAsync"/>.</summary>
public sealed record LlmBotSessionRunResult(LlmBotSessionStopReason Reason, string? Detail);

/// <summary>
/// Прогоняет игровую сессию силами набора <see cref="LlmBot"/> от текущего состояния до конца —
/// асинхронный аналог <c>Game.Bots.BotSessionRunner</c> (тот — для формульного <c>SimpleBot</c>,
/// синхронный; здесь каждый ход требует реального сетевого вызова к LLM, поэтому цикл асинхронный).
/// Боты работают строго последовательно, бот за ботом (решение пользователя, чтобы порядок ходов был
/// детерминированным и логи — читаемыми) — никакого параллелизма между ботами внутри одной фазы
/// решений.
/// </summary>
public static class LlmBotSessionRunner
{
    /// <summary>
    /// <paramref name="onTurnCompleted"/> — необязательный колбэк сразу после того, как все боты
    /// приняли решения на ходу, но до перехода к следующему (удобно для промежуточных отчётов на
    /// длинном прогоне — типичный ход занимает секунды-десятки секунд на одного бота).
    /// <paramref name="maxConsecutiveExhaustedTurns"/> — запрос пользователя 2026-08-16 (живой
    /// прогон стадии 1 честно доехал до конца 12-ходовой сессии, из которых 9 ходов подряд бот не
    /// смог получить ни одного валидного ответа — «нет смысла гнать ходы до конца», если бот явно
    /// застрял): если ОДИН бот подряд столько раз исчерпал попытки хода
    /// (<see cref="LlmBotTurnOutcome.Exhausted"/>), прогон останавливается сразу, не дожидаясь конца
    /// сессии — дальнейшие ходы тем же бэкендом/промптом почти наверняка так же бесполезны.
    /// </summary>
    public static async Task<LlmBotSessionRunResult> RunToCompletionAsync(
        GameSession session,
        IReadOnlyList<LlmBot> bots,
        Random random,
        BotDecisionLog log,
        BotMetricsLog? metricsLog = null,
        Action<GameSession>? onTurnCompleted = null,
        int maxConsecutiveExhaustedTurns = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(log);
        if (maxConsecutiveExhaustedTurns < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConsecutiveExhaustedTurns), maxConsecutiveExhaustedTurns, "Must allow at least one exhausted turn before stopping.");
        }

        var consecutiveExhaustedByTeam = new Dictionary<Ulid, int>();

        while (!session.State.IsFinished)
        {
            switch (session.State.CurrentPhase)
            {
                case TurnPhase.Settlement:
                    session.RunTick(random);
                    session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
                    break;

                case TurnPhase.Decision:
                    foreach (var bot in bots)
                    {
                        var result = await bot.TakeTurnAsync(session, log, metricsLog, cancellationToken).ConfigureAwait(false);

                        if (result.Outcome != LlmBotTurnOutcome.Exhausted)
                        {
                            consecutiveExhaustedByTeam[bot.TeamId] = 0;
                            continue;
                        }

                        var streak = consecutiveExhaustedByTeam.GetValueOrDefault(bot.TeamId) + 1;
                        consecutiveExhaustedByTeam[bot.TeamId] = streak;
                        if (streak >= maxConsecutiveExhaustedTurns)
                        {
                            return new LlmBotSessionRunResult(
                                LlmBotSessionStopReason.RepeatedFailures,
                                $"Team '{bot.TeamId}' failed to produce a valid command on {streak} consecutive turns (turn {session.State.CurrentTurn}).");
                        }
                    }

                    // До AdvancePhase, не после: тот сразу увеличивает CurrentTurn (см.
                    // PhaseAdvanced) — колбэк должен видеть номер хода, на котором боты только что
                    // решали, а не следующего.
                    onTurnCompleted?.Invoke(session);
                    session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
                    break;
            }
        }

        return new LlmBotSessionRunResult(LlmBotSessionStopReason.Completed, null);
    }
}
