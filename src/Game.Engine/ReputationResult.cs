namespace Game.Engine;

/// <summary>Результат расчёта публичной репутации команды (<see cref="ReputationCalculator.Calculate"/>).</summary>
public sealed record ReputationResult
{
    /// <summary>Процент исполненных поставок с учётом затухания и тяжести несоблюдений (0..100).</summary>
    public required decimal Percentage { get; init; }

    /// <summary>
    /// Сколько отдельных фактов (успешных поставок, срывов, расторжений) вошло в расчёт — для
    /// оценки статистической значимости процента (SPEC §7: «% вместе с количеством»). Не учитывает
    /// «пристрелочные» срывы — те не идут в публичную репутацию вовсе, как будто их не было.
    /// </summary>
    public required int SampleCount { get; init; }
}
