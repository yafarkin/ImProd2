using Game.Engine;

namespace Game.Bots.Llm;

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
    /// </summary>
    public static async Task RunToCompletionAsync(
        GameSession session,
        IReadOnlyList<LlmBot> bots,
        Random random,
        BotDecisionLog log,
        BotMetricsLog? metricsLog = null,
        Action<GameSession>? onTurnCompleted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(log);

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
                        await bot.TakeTurnAsync(session, log, metricsLog, cancellationToken).ConfigureAwait(false);
                    }

                    // До AdvancePhase, не после: тот сразу увеличивает CurrentTurn (см.
                    // PhaseAdvanced) — колбэк должен видеть номер хода, на котором боты только что
                    // решали, а не следующего.
                    onTurnCompleted?.Invoke(session);
                    session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
                    break;
            }
        }
    }
}
