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

    /// <summary>
    /// Досрочный переход фазы по команде ведущего («Ускорить фазу»). Голый
    /// <see cref="GameSession.AdvancePhase"/> здесь не годится: расчёт считает только
    /// <see cref="TryAdvance"/>, а тот на паузе молчит и опрашивается раз в секунду, — поэтому ведущий,
    /// ускоряющий фазы на паузе или просто двумя быстрыми нажатиями, проводил сессию через
    /// <see cref="TurnPhase.Settlement"/> без расчёта: ход проходил без зарплат, производства и поставок.
    /// Тут расчёт гарантирован с обеих сторон перехода: досчитывается перед уходом из
    /// <see cref="TurnPhase.Settlement"/>, если ещё не был посчитан, и считается сразу при входе в неё —
    /// даже на паузе, раз ведущий двигает фазы явно.
    /// </summary>
    public static void AdvanceByFacilitator(GameSession session, Random newsRandom)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(newsRandom);

        EnsureCalculationTickRan(session, newsRandom);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        EnsureCalculationTickRan(session, newsRandom);
    }

    private static void EnsureCalculationTickRan(GameSession session, Random newsRandom)
    {
        if (!session.State.IsFinished
            && session.State.CurrentPhase == TurnPhase.Settlement
            && !PhaseTimerCalculator.CalculationTickAlreadyRanForCurrentPhase(session))
        {
            session.RunTick(newsRandom);
        }
    }
}
