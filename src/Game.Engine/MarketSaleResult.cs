namespace Game.Engine;

/// <summary>Результат расчёта продажи материала системе (<see cref="MarketSaleCalculator.Calculate"/>).</summary>
public sealed record MarketSaleResult
{
    /// <summary>Объём, проданный в пределах оставшейся на этот ход ёмкости — по полной цене.</summary>
    public required decimal WithinCapacityVolume { get; init; }

    /// <summary>Объём сверх оставшейся ёмкости — по цене с понижающим коэффициентом.</summary>
    public required decimal OverflowVolume { get; init; }

    /// <summary>Цена за единицу в пределах ёмкости (котировка × множитель маржи передела), для аудита.</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>Итоговая выручка от продажи.</summary>
    public required decimal TotalRevenue { get; init; }
}
