namespace Game.Config.Economy;

/// <summary>
/// Параметры внешней экономики сессии (SPEC §5.3-5.5): аварийная закупка, маржа по переделу,
/// понижающий коэффициент за превышение ёмкости рынка, электричество, сценарный тренд.
/// Все числа — заглушки, требуют калибровки.
/// </summary>
public sealed record EconomyConfig
{
    /// <summary>Множитель к текущей рыночной цене материала при аварийной закупке у системы.</summary>
    public required decimal EmergencyPurchasePriceMultiplier { get; init; }

    /// <summary>Множители маржи по уровню передела.</summary>
    public required IReadOnlyList<ProcessingLevelMarginConfig> MarginMultiplierByProcessingLevel { get; init; }

    /// <summary>Понижающий коэффициент цены при продаже сверх ёмкости рынка (0..1).</summary>
    public required decimal MarketCapacityOverflowDiscount { get; init; }

    /// <summary>Базовая цена электричества (потребляется фабриками уровня 2 и выше).</summary>
    public required decimal ElectricityBasePrice { get; init; }

    /// <summary>Сценарный тренд сессии, разбитый на отрезки ходов.</summary>
    public required IReadOnlyList<EconomyTrendPhaseConfig> TrendScenario { get; init; }
}
