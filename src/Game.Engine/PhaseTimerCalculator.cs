namespace Game.Engine;

/// <summary>
/// Внешний серверный таймер-сервис, которого ждёт <see cref="GameSessionState"/> (см. её
/// док-комментарий): сама сессия не хранит настенное время, поэтому остаток фазы и признак «тик уже
/// посчитан» выводятся здесь заново из <see cref="GameSession.Entries"/> при каждом обращении — тот
/// же приём, что и у <see cref="GameSession.GetReputation"/>. Использует <see
/// cref="EventLogEntry{TState}.Timestamp"/> — по его же док-комментарию это честная область
/// применения («дебрифинг/аудит и разбор инцидентов»), а восстановление отсчёта после перезапуска
/// процесса ровно такой инцидент.
/// </summary>
public static class PhaseTimerCalculator
{
    /// <summary>
    /// Сколько времени осталось до конца текущей фазы. Может быть отрицательным, если фазу давно
    /// пора было сменить — вызывающая сторона (<see cref="PhaseAutoAdvancer"/>) просто перейдёт на
    /// следующем тике опроса. Ноль, если сессия уже завершена — текущей фазы, по которой можно было
    /// бы отсчитывать, больше нет.
    /// </summary>
    public static TimeSpan Remaining(GameSession session, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);

        var state = session.State;
        if (state.IsFinished)
        {
            return TimeSpan.Zero;
        }

        var boundaryIndex = FindPhaseBoundaryIndex(session.Entries);
        var phaseStartedAt = session.Entries[boundaryIndex].Timestamp;

        var pausedDuration = TimeSpan.Zero;
        DateTimeOffset? activePauseStartedAt = null;
        for (var i = boundaryIndex + 1; i < session.Entries.Count; i++)
        {
            var entry = session.Entries[i];
            if (entry.Change is SessionPaused)
            {
                activePauseStartedAt = entry.Timestamp;
            }
            else if (entry.Change is SessionResumed && activePauseStartedAt is { } pauseStartedAt)
            {
                pausedDuration += entry.Timestamp - pauseStartedAt;
                activePauseStartedAt = null;
            }
        }

        var effectiveNow = activePauseStartedAt ?? now;
        var elapsed = effectiveNow - phaseStartedAt - pausedDuration;

        var totalAllowed = BaseDuration(state.CurrentPhase, state.Config.Raw.PhaseTiming) + state.PhaseExtensionSeconds;
        return totalAllowed - elapsed;
    }

    /// <summary>
    /// Был ли уже посчитан тик расчёта для текущего пребывания сессии в фазе <see
    /// cref="TurnPhase.Calculation"/> — по наличию <see cref="MarketUpdated"/> с момента последней
    /// границы фазы (<see cref="GameSession.RunTick"/> дописывает его безусловно, даже без единой
    /// команды в сессии). Признак выводится из самого журнала, а не хранится отдельным флагом в
    /// памяти — иначе перезапуск процесса между <see cref="GameSession.RunTick"/> и последующим <see
    /// cref="GameSession.AdvancePhase"/> сбросил бы такой флаг и мог бы запустить тик повторно
    /// (задвоение финансов и производства за ход).
    /// </summary>
    public static bool CalculationTickAlreadyRanForCurrentPhase(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var boundaryIndex = FindPhaseBoundaryIndex(session.Entries);
        for (var i = boundaryIndex + 1; i < session.Entries.Count; i++)
        {
            if (session.Entries[i].Change is MarketUpdated)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Индекс последней записи-границы фазы: <see cref="SessionStarted"/> (сессия только началась)
    /// либо <see cref="PhaseAdvanced"/> (переход только что произошёл) — в обоих случаях собственная
    /// метка времени записи и есть момент начала текущей фазы.
    /// </summary>
    private static int FindPhaseBoundaryIndex(IReadOnlyList<EventLogEntry<GameSessionState>> entries)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Change is SessionStarted or PhaseAdvanced)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Session journal has no phase boundary entry (no SessionStarted found).");
    }

    private static TimeSpan BaseDuration(TurnPhase phase, Game.Config.Session.PhaseTimingConfig timing) => phase switch
    {
        TurnPhase.Calculation => TimeSpan.FromSeconds(timing.CalculationPhaseSeconds),
        TurnPhase.Decision => TimeSpan.FromSeconds(timing.DecisionPhaseSeconds),
        TurnPhase.Closing => TimeSpan.FromSeconds(timing.CompletionPhaseSeconds),
        _ => throw new InvalidOperationException($"Unknown turn phase '{phase}'.")
    };
}
