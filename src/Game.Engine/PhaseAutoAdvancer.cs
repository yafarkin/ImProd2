namespace Game.Engine;

/// <summary>
/// Оркестровка автоматического перехода фаз по таймеру (Блок 8.2, SPEC §4, §11) — решает, пора ли
/// сессии перейти дальше, и переводит это решение в уже существующие, уже протестированные вызовы
/// <see cref="GameSession.RunTick"/>/<see cref="GameSession.AdvancePhase"/>. Сама не читает часы ОС и
/// ничего не блокирует — <paramref name="now"/> вызывающей стороны (обычно фонового сервиса
/// <c>Game.Web.PhaseTimerBackgroundService</c>), которая и отвечает за периодичность опроса и
/// потокобезопасность записи в журнал.
/// </summary>
public static class PhaseAutoAdvancer
{
    /// <summary>
    /// Пробует перевести сессию дальше, если время текущей фазы истекло. Возвращает <c>true</c>,
    /// если сессия действительно была переведена (тик посчитан и/или фаза сменилась).
    /// </summary>
    public static bool TryAdvance(GameSession session, DateTimeOffset now, Random newsRandom)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(newsRandom);

        if (session.State.IsFinished || session.State.IsPaused)
        {
            return false;
        }

        if (PhaseTimerCalculator.Remaining(session, now) > TimeSpan.Zero)
        {
            return false;
        }

        if (session.State.CurrentPhase == TurnPhase.Calculation
            && !PhaseTimerCalculator.CalculationTickAlreadyRanForCurrentPhase(session))
        {
            session.RunTick(newsRandom);
        }

        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        return true;
    }
}
