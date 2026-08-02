namespace Game.Config.Session;

/// <summary>
/// Длительности двух фаз хода (SPEC §4: расчёт+завершение → решения). Расчёт и завершение раньше
/// были отдельными фазами, но сам расчёт мгновенен и считается атомарно сразу при входе в фазу — обе
/// слиты в одну (<see cref="Game.Engine.TurnPhase.Settlement"/>), read-only буфер после расчёта
/// исключает гонку «кто успел кликнуть последним». Все числа — заглушки, требуют калибровки.
/// </summary>
public sealed record PhaseTimingConfig
{
    /// <summary>Длительность фазы расчёта+завершения в секундах.</summary>
    public required int SettlementPhaseSeconds { get; init; }

    /// <summary>Длительность фазы решений в секундах.</summary>
    public required int DecisionPhaseSeconds { get; init; }
}
