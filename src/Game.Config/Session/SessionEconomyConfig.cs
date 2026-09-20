using Game.Config.Economy;

namespace Game.Config.Session;

/// <summary>
/// Сессионная часть <see cref="EconomyConfig"/> — те же поля, кроме <see
/// cref="EconomyConfig.BaseMarketPerMaterial"/>: он привязан к конкретным материалам производственной
/// модели (<see cref="Game.Config.ProductionModel.ProductionModelConfig.BaseMarketPerMaterial"/>), а
/// не к темпу/сложности сессии. <see cref="Game.Config.Loading.GameConfigComposer"/> собирает из
/// этого типа и <see cref="Game.Config.ProductionModel.ProductionModelConfig.BaseMarketPerMaterial"/>
/// полноценный <see cref="EconomyConfig"/> — весь остальной код по-прежнему видит только его, не
/// этот тип напрямую.
/// </summary>
public sealed record SessionEconomyConfig
{
    /// <summary>См. <see cref="EconomyConfig.EmergencyPurchaseBaseMultiplier"/>.</summary>
    public required decimal EmergencyPurchaseBaseMultiplier { get; init; }

    /// <summary>См. <see cref="EconomyConfig.EmergencyPurchasePressureMultiplierPerUnit"/>.</summary>
    public required decimal EmergencyPurchasePressureMultiplierPerUnit { get; init; }

    /// <summary>См. <see cref="EconomyConfig.EmergencyPurchasePressureHalfLifeTurns"/>.</summary>
    public required int EmergencyPurchasePressureHalfLifeTurns { get; init; }

    /// <summary>См. <see cref="EconomyConfig.MarketCapacityOverflowDiscount"/>.</summary>
    public required decimal MarketCapacityOverflowDiscount { get; init; }

    /// <summary>См. <see cref="EconomyConfig.PricingModel"/>.</summary>
    public PricingModel PricingModel { get; init; } = PricingModel.CostPlus;

    /// <summary>См. <see cref="EconomyConfig.MarketPriceFloorRate"/>.</summary>
    public decimal MarketPriceFloorRate { get; init; } = 0.35m;

    /// <summary>См. <see cref="EconomyConfig.MarketCapacityScale"/>.</summary>
    public decimal MarketCapacityScale { get; init; } = 1m;

    /// <summary>См. <see cref="EconomyConfig.MarketSupplyPressureHalfLifeTurns"/>.</summary>
    public int MarketSupplyPressureHalfLifeTurns { get; init; } = 3;

    /// <summary>См. <see cref="EconomyConfig.EconomyIndexMin"/>.</summary>
    public decimal EconomyIndexMin { get; init; } = 0.85m;

    /// <summary>См. <see cref="EconomyConfig.EconomyIndexMax"/>.</summary>
    public decimal EconomyIndexMax { get; init; } = 1.15m;

    /// <summary>См. <see cref="EconomyConfig.ElectricityBasePrice"/>.</summary>
    public required decimal ElectricityBasePrice { get; init; }

    /// <summary>См. <see cref="EconomyConfig.ElectricityConsumptionPerOutputUnit"/>.</summary>
    public required decimal ElectricityConsumptionPerOutputUnit { get; init; }

    /// <summary>См. <see cref="EconomyConfig.TrendScenario"/>.</summary>
    public required IReadOnlyList<EconomyTrendPhaseConfig> TrendScenario { get; init; }

    /// <summary>См. <see cref="EconomyConfig.WarehouseLiquidationRate"/>.</summary>
    public required decimal WarehouseLiquidationRate { get; init; }
}
