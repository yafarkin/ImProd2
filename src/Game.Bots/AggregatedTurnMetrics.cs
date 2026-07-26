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
}
