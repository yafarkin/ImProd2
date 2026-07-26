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
            EmergencyPurchasePriceMultiplier = 1m,
            BaseMarketPerMaterial = Array.Empty<MaterialMarketConfig>(),
            MarginMultiplierByProcessingLevel = new[]
            {
                new ProcessingLevelMarginConfig { Level = 1, MarginMultiplier = 1.2m },
            },
            MarketCapacityOverflowDiscount = 0.5m,
            ElectricityBasePrice = 1m,
            TrendScenario = Array.Empty<EconomyTrendPhaseConfig>(),
            WarehouseLiquidationRate = 0.5m,
        };
    }

    private static Market BuildMarket(string materialId, decimal price, decimal capacity)
    {
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote> { [materialId] = new(price, capacity) }, electricityPrice: 0m);
        return market;
    }

    [Fact]
    public void A_Sale_Within_Capacity_Is_Priced_At_The_Full_Quote()
    {
        var market = BuildMarket("ore", price: 10m, capacity: 100m);

        var result = MarketSaleCalculator.Calculate(market, BuildEconomy(), RawMaterial, volume: 20m);

        Assert.Equal(20m, result.WithinCapacityVolume);
        Assert.Equal(0m, result.OverflowVolume);
        Assert.Equal(10m, result.UnitPrice); // уровень 0 -> множитель по умолчанию 1
        Assert.Equal(200m, result.TotalRevenue);
    }

    [Fact]
    public void A_Sale_Straddling_The_Remaining_Capacity_Splits_Into_Full_Price_And_Discounted_Parts()
    {
        var market = BuildMarket("ore", price: 10m, capacity: 8m);

        var result = MarketSaleCalculator.Calculate(market, BuildEconomy(), RawMaterial, volume: 10m);

        Assert.Equal(8m, result.WithinCapacityVolume);
        Assert.Equal(2m, result.OverflowVolume);
        Assert.Equal(10m, result.UnitPrice);
        // 8 * 10 + 2 * (10 * 0.5) = 80 + 10 = 90 (перепроизводство обваливает цену за лишние 2 единицы)
        Assert.Equal(90m, result.TotalRevenue);
    }

    [Fact]
    public void A_Sale_Already_Beyond_Consumed_Capacity_Is_Priced_Entirely_At_The_Discount()
    {
        var market = BuildMarket("ore", price: 10m, capacity: 5m);
        market.RecordSale("ore", 5m); // ёмкость этого хода уже выбрана предыдущей продажей

        var result = MarketSaleCalculator.Calculate(market, BuildEconomy(), RawMaterial, volume: 4m);

        Assert.Equal(0m, result.WithinCapacityVolume);
        Assert.Equal(4m, result.OverflowVolume);
        Assert.Equal(20m, result.TotalRevenue); // 4 * (10 * 0.5)
    }

    [Fact]
    public void Higher_Processing_Level_Sells_With_Its_Configured_Margin_Multiplier()
    {
        var market = BuildMarket("sheet", price: 10m, capacity: 100m);

        var result = MarketSaleCalculator.Calculate(market, BuildEconomy(), ProcessedMaterial, volume: 5m);

        Assert.Equal(12m, result.UnitPrice); // 10 * 1.2 (уровень 1 сконфигурирован)
        Assert.Equal(60m, result.TotalRevenue);
    }
}
