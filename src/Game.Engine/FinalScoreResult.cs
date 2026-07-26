namespace Game.Engine;

/// <summary>
/// Итоговый счёт команды по ликвидационной стоимости (SPEC §5.11): <c>Кэш − Долг + Склад + Фабрики</c>.
/// Несёт разбивку по слагаемым, а не только сумму, — экраны отчёта не обязаны её пересчитывать.
/// </summary>
public sealed record FinalScoreResult
{
    /// <summary>Команда, для которой посчитан счёт.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Денежный остаток.</summary>
    public required decimal Cash { get; init; }

    /// <summary>Непогашенный долг.</summary>
    public required decimal Debt { get; init; }

    /// <summary>Оценка склада (сумма по материалам: остаток × текущая рыночная цена × <c>EconomyConfig.WarehouseLiquidationRate</c>).</summary>
    public required decimal WarehouseValue { get; init; }

    /// <summary>Оценка фабрик (сумма по фабрикам: <c>BuildCost × LiquidationValueCoefficient</c> её типа; R&amp;D не учитывается, SPEC §5.11).</summary>
    public required decimal FactoriesValue { get; init; }

    /// <summary>Итоговый счёт: <c>Cash − Debt + WarehouseValue + FactoriesValue</c>.</summary>
    public required decimal Score { get; init; }
}
