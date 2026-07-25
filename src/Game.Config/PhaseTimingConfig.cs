namespace Game.Config;

/// <summary>
/// Длительности трёх фаз хода (SPEC §4: расчёт → решения → завершение). Завершение — короткое
/// read-only окно перед фиксацией, чтобы исключить гонку «кто успел кликнуть последним».
/// Все числа — заглушки, требуют калибровки.
/// </summary>
public sealed record PhaseTimingConfig
{
    /// <summary>Длительность фазы расчёта в секундах.</summary>
    public required int CalculationPhaseSeconds { get; init; }

    /// <summary>Длительность фазы решений в секундах.</summary>
    public required int DecisionPhaseSeconds { get; init; }

    /// <summary>Длительность read-only фазы завершения в секундах.</summary>
    public required int CompletionPhaseSeconds { get; init; }
}
