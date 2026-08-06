using Game.Config.Economy;

namespace Game.Engine.Tests;

public class RndCalculatorTests
{
    private static readonly RndConfig Config = new()
    {
        CumulativeInvestmentThresholdsByLevel = new[] { 100m, 300m, 600m }, // 1->2, 2->3, 3->4
        ProductionRateBonusPerLevel = 0.1m,
        MaxCommitmentPerTurn = 1000m,
    };

    [Fact]
    public void CalculateResultingLevel_Stays_At_Current_Level_Below_The_Next_Threshold()
    {
        Assert.Equal(1, RndCalculator.CalculateResultingLevel(currentLevel: 1, cumulativeInvestment: 99m, Config));
    }

    [Fact]
    public void CalculateResultingLevel_Advances_One_Level_Exactly_At_The_Threshold()
    {
        Assert.Equal(2, RndCalculator.CalculateResultingLevel(currentLevel: 1, cumulativeInvestment: 100m, Config));
    }

    [Fact]
    public void CalculateResultingLevel_Advances_Multiple_Levels_At_Once_For_A_Large_Investment()
    {
        // 100 (1->2) + 300 (2->3) = 400 покрывает оба порога разом.
        Assert.Equal(3, RndCalculator.CalculateResultingLevel(currentLevel: 1, cumulativeInvestment: 400m, Config));
    }

    [Fact]
    public void CalculateResultingLevel_Stops_At_The_Highest_Configured_Level()
    {
        Assert.Equal(4, RndCalculator.CalculateResultingLevel(currentLevel: 1, cumulativeInvestment: 100_000m, Config));
    }
}
