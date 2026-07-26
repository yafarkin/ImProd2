using Game.Config.Economy;

namespace Game.Engine.Tests;

/// <summary>Чистая функция «ход → (цена, ёмкость) по материалу» (Блок 6.1, SPEC §5.4-5.5).</summary>
public class MarketCalculatorTests
{
    private static EconomyConfig BuildEconomy(IReadOnlyList<EconomyTrendPhaseConfig> trend)
    {
        return new EconomyConfig
        {
            EmergencyPurchasePriceMultiplier = 1m,
            BaseMarketPerMaterial = new[]
            {
                new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 100m },
            },
            MarginMultiplierByProcessingLevel = Array.Empty<ProcessingLevelMarginConfig>(),
            MarketCapacityOverflowDiscount = 0.5m,
            ElectricityBasePrice = 5m,
            TrendScenario = trend,
        };
    }

    [Fact]
    public void With_No_Trend_Phases_Price_And_Capacity_Stay_At_Their_Base_On_Any_Turn()
    {
        var economy = BuildEconomy(Array.Empty<EconomyTrendPhaseConfig>());

        var result = MarketCalculator.Calculate(turn: 50, economy);

        Assert.Equal(10m, result.Quotes["ore"].Price);
        Assert.Equal(100m, result.Quotes["ore"].Capacity);
        Assert.Equal(5m, result.ElectricityPrice);
    }

    [Fact]
    public void An_Upswing_Accumulates_Price_And_Capacity_Turn_By_Turn_From_Turn_One()
    {
        var economy = BuildEconomy(new[]
        {
            new EconomyTrendPhaseConfig { Trend = EconomyTrend.Up, StartTurn = 1, EndTurn = 10, PriceChangePerTurn = 1m, CapacityChangePerTurn = 2m },
        });

        var turnOne = MarketCalculator.Calculate(1, economy);
        var turnThree = MarketCalculator.Calculate(3, economy);

        Assert.Equal(11m, turnOne.Quotes["ore"].Price); // 10 + 1*1
        Assert.Equal(102m, turnOne.Quotes["ore"].Capacity);
        Assert.Equal(13m, turnThree.Quotes["ore"].Price); // 10 + 1*3
        Assert.Equal(106m, turnThree.Quotes["ore"].Capacity);
        Assert.Equal(8m, turnThree.ElectricityPrice); // 5 + 1*3
    }

    [Fact]
    public void A_Downswing_Never_Drives_Price_Or_Capacity_Below_Zero()
    {
        var economy = BuildEconomy(new[]
        {
            new EconomyTrendPhaseConfig { Trend = EconomyTrend.Down, StartTurn = 1, EndTurn = 100, PriceChangePerTurn = -5m, CapacityChangePerTurn = -50m },
        });

        var result = MarketCalculator.Calculate(turn: 20, economy);

        Assert.Equal(0m, result.Quotes["ore"].Price);
        Assert.Equal(0m, result.Quotes["ore"].Capacity);
        Assert.Equal(0m, result.ElectricityPrice);
    }

    [Fact]
    public void Turns_Outside_Any_Configured_Trend_Phase_Do_Not_Move_The_Market()
    {
        // Тренд задан только на ходы 5-6; до и после экономика стоит на месте.
        var economy = BuildEconomy(new[]
        {
            new EconomyTrendPhaseConfig { Trend = EconomyTrend.Up, StartTurn = 5, EndTurn = 6, PriceChangePerTurn = 10m, CapacityChangePerTurn = 0m },
        });

        var beforePhase = MarketCalculator.Calculate(4, economy);
        var afterPhase = MarketCalculator.Calculate(7, economy);

        Assert.Equal(10m, beforePhase.Quotes["ore"].Price);
        Assert.Equal(30m, afterPhase.Quotes["ore"].Price); // 10 + 10*2 (только ходы 5 и 6 двигали цену)
    }

    [Fact]
    public void Recomputing_The_Same_Turn_Twice_Yields_The_Same_Result()
    {
        var economy = BuildEconomy(new[]
        {
            new EconomyTrendPhaseConfig { Trend = EconomyTrend.Up, StartTurn = 1, EndTurn = 10, PriceChangePerTurn = 1m, CapacityChangePerTurn = 2m },
        });

        var first = MarketCalculator.Calculate(4, economy);
        var second = MarketCalculator.Calculate(4, economy);

        Assert.Equal(first.Quotes["ore"].Price, second.Quotes["ore"].Price);
        Assert.Equal(first.Quotes["ore"].Capacity, second.Quotes["ore"].Capacity);
    }
}
