namespace Game.Bots;

/// <summary>Метрики хода, усреднённые по всем партиям, которые до него дожили (Блок 7.2).</summary>
public sealed record AggregatedTurnMetrics
{
    /// <summary>Ход, на который относятся метрики.</summary>
    public required int Turn { get; init; }

    /// <summary>Средняя по партиям денежная масса на этот ход.</summary>
    public required decimal AverageTotalCash { get; init; }

    /// <summary>Средний по партиям объём, проданный системе за этот ход.</summary>
    public required decimal AverageVolumeSoldToSystem { get; init; }

    /// <summary>Сколько из прогнанных партий дожили до этого хода (более короткие партии не участвуют в среднем дальше своего последнего хода).</summary>
    public required int SessionCount { get; init; }

    /// <summary>Среднее по партиям среднее состояние фабрик (SPEC §5.6) на этот ход.</summary>
    public required decimal AverageFactoryCondition { get; init; }

    /// <summary>Среднее по партиям число фабрик на вынужденном простое на этот ход.</summary>
    public required decimal AverageFactoriesUnderRepairCount { get; init; }

    /// <summary>
    /// Средняя по партиям сходимость к идеальному залу на этот ход (Блок 7.3.5) — временной ряд
    /// Score(t)/X(t) для дебрифа (<c>docs/balancing-bots.md</c> §3, «Траектория по времени»).
    /// <c>null</c>, если ни у одной партии не было идеального зала на входе.
    /// </summary>
    public decimal? AverageConvergence { get; init; }
}
