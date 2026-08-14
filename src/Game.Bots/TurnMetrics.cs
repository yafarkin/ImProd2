namespace Game.Bots;

/// <summary>Метрики одного хода одной партии (Блок 7.2, харнесс балансировки).</summary>
public sealed record TurnMetrics
{
    /// <summary>Ход, на который относятся метрики.</summary>
    public required int Turn { get; init; }

    /// <summary>Денежная масса на этот ход — сумма остатков всех команд (риск неконтролируемого роста).</summary>
    public required decimal TotalCash { get; init; }

    /// <summary>Объём, проданный системе за этот ход всеми командами вместе (throughput).</summary>
    public required decimal VolumeSoldToSystem { get; init; }

    /// <summary>Сколько команд на этом ходу не смогли расплатиться и получили принудительный заём (прокси «дефолта»).</summary>
    public required int ForcedLoanCount { get; init; }

    /// <summary>Среднее состояние (<c>Factory.Condition</c>) по всем построенным фабрикам всех команд на этот ход (SPEC §5.6) — 1.0, если фабрик ещё нет.</summary>
    public required decimal AverageFactoryCondition { get; init; }

    /// <summary>Сколько фабрик всех команд на вынужденном простое на этот ход.</summary>
    public required int FactoriesUnderRepairCount { get; init; }

    /// <summary>Сколько фабрик всех команд пересекло критический порог и ушло в простой именно на этом ходу.</summary>
    public required int ForcedRepairEventsCount { get; init; }

    /// <summary>
    /// Средняя по командам сходимость к идеальному залу на этот ход — Score(t)/X(t), где X(t) взят из
    /// заранее прогнанного <see cref="Game.Engine.IdealHallCalculator"/> той же ветки команды (Блок
    /// 7.3.5, <c>docs/balancing-bots.md</c> §3). <c>null</c>, если харнесс запущен без идеального зала
    /// (X(t) не передан) — не то же самое, что 0.
    /// </summary>
    public decimal? AverageConvergence { get; init; }
}
