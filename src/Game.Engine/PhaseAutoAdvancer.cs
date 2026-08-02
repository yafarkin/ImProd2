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
    /// Пробует перевести сессию дальше. Расчёт (<see cref="GameSession.RunTick"/>) — не «в течение»
    /// отведённого фазе времени, а сразу при входе в <see cref="TurnPhase.Settlement"/>: как только
    /// обнаружено, что он ещё не посчитан для текущего пребывания в фазе, считаем его немедленно, не
    /// дожидаясь истечения таймера, — командам должны быть видны свежие результаты весь буфер
    /// <see cref="TurnPhase.Settlement"/>, а не только в момент начала следующих решений. Сам переход
    /// фазы (<see cref="GameSession.AdvancePhase"/>) по-прежнему ждёт полного истечения таймера —
    /// проверка тика идёт первой и независимо от остатка времени, поэтому переживает и восстановление
    /// после сбоя процесса ровно между входом в фазу и расчётом (тот же приём, что раньше давала
    /// проверка "тик ещё не посчитан", просто без привязки к истечению таймера).
    /// Возвращает <c>true</c>, если сессия действительно была переведена (тик посчитан и/или фаза
    /// сменилась).
    /// </summary>
    public static bool TryAdvance(GameSession session, DateTimeOffset now, Random newsRandom)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(newsRandom);

        if (session.State.IsFinished || session.State.IsPaused)
        {
            return false;
        }

        if (session.State.CurrentPhase == TurnPhase.Settlement
            && !PhaseTimerCalculator.CalculationTickAlreadyRanForCurrentPhase(session))
        {
            session.RunTick(newsRandom);
            return true;
        }

        if (PhaseTimerCalculator.Remaining(session, now) > TimeSpan.Zero)
        {
            return false;
        }

        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        return true;
    }
}
