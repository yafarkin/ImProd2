using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Расчёт продажи материала системе (Блок 6.1, SPEC §5.4): ёмкость, понижающий коэффициент, маржа передела.</summary>
public class MarketSaleCalculatorTests
{
    private static readonly Sector Sector = new("A", "Sector A");
    private static readonly Material RawMaterial = new("ore", "Ore", Sector, level: 0);
    private static readonly Material ProcessedMaterial = new("sheet", "Sheet", Sector, level: 1);

    private static EconomyConfig BuildEconomy()
    {
        return new EconomyConfig
        {
            EmergencyPurchaseBaseMultiplier = 1m,
            EmergencyPurchasePressureMultiplierPerUnit = 0m,
            EmergencyPurchasePressureHalfLifeTurns = 1,
            BaseMarketPerMaterial = Array.Empty<MaterialMarketConfig>(),
            MarginMultiplierByProcessingLevel = new[]
            {
                new ProcessingLevelMarginConfig { Level = 1, MarginMultiplier = 1.2m },
            },
            MarketCapacityOverflowDiscount = 0.5m,
            ElectricityBasePrice = 1m,
            ElectricityConsumptionPerOutputUnit = 0m,
            TrendScenario = Array.Empty<EconomyTrendPhaseConfig>(),
            WarehouseLiquidationRate = 0.5m,
        };
    }

    /// <summary>Ёмкость по-прежнему на рынке (см. doc-comment MarketSaleCalculator) — цена больше не оттуда, только капасити.</summary>
    private static Market BuildMarket(string materialId, decimal capacity)
    {
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote> { [materialId] = new(price: 0m, capacity) }, electricityPrice: 0m);
        return market;
    }

    private static IReadOnlyDictionary<string, decimal> MaterialCosts(string materialId, decimal unitCost) =>
        new Dictionary<string, decimal> { [materialId] = unitCost };

    [Fact]
    public void A_Sale_Within_Capacity_Is_Priced_At_The_Full_Cost()
    {
        var market = BuildMarket("ore", capacity: 100m);

        var result = MarketSaleCalculator.Calculate(market, MaterialCosts("ore", 100m), BuildEconomy(), RawMaterial, volume: 20m);

        Assert.Equal(20m, result.WithinCapacityVolume);
        Assert.Equal(0m, result.OverflowVolume);
        Assert.Equal(105m, result.UnitPrice); // уровень 0 -> множитель по умолчанию DefaultMarginMultiplier (1.05)
        Assert.Equal(2100m, result.TotalRevenue);
    }

    [Fact]
    public void A_Sale_Straddling_The_Remaining_Capacity_Splits_Into_Full_Price_And_Discounted_Parts()
    {
        var market = BuildMarket("ore", capacity: 8m);

        var result = MarketSaleCalculator.Calculate(market, MaterialCosts("ore", 100m), BuildEconomy(), RawMaterial, volume: 10m);

        Assert.Equal(8m, result.WithinCapacityVolume);
        Assert.Equal(2m, result.OverflowVolume);
        Assert.Equal(105m, result.UnitPrice);
        // 8 * 105 + 2 * (105 * 0.5) = 840 + 105 = 945 (перепроизводство обваливает цену за лишние 2 единицы)
        Assert.Equal(945m, result.TotalRevenue);
    }

    [Fact]
    public void A_Sale_Already_Beyond_Consumed_Capacity_Is_Priced_Entirely_At_The_Discount()
    {
        var market = BuildMarket("ore", capacity: 5m);
        market.RecordSale("ore", 5m); // ёмкость этого хода уже выбрана предыдущей продажей

        var result = MarketSaleCalculator.Calculate(market, MaterialCosts("ore", 100m), BuildEconomy(), RawMaterial, volume: 4m);

        Assert.Equal(0m, result.WithinCapacityVolume);
        Assert.Equal(4m, result.OverflowVolume);
        Assert.Equal(210m, result.TotalRevenue); // 4 * (105 * 0.5)
    }

    [Fact]
    public void Higher_Processing_Level_Sells_With_Its_Configured_Margin_Multiplier()
    {
        var market = BuildMarket("sheet", capacity: 100m);

        var result = MarketSaleCalculator.Calculate(market, MaterialCosts("sheet", 10m), BuildEconomy(), ProcessedMaterial, volume: 5m);

        Assert.Equal(12m, result.UnitPrice); // 10 * 1.2 (уровень 1 сконфигурирован)
        Assert.Equal(60m, result.TotalRevenue);
    }
}
