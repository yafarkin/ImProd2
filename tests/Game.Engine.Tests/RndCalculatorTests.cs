using Game.Config.Economy;

namespace Game.Engine.Tests;

public class RndCalculatorTests
{
    private static readonly RndConfig Config = new()
    {
        // sqrt(100)=10, sqrt(400)=20, sqrt(900)=30 — накопленные ¤: 100, 400, 900.
        ResearchPointThresholdsByLevel = new[] { 10m, 20m, 30m }, // 1->2, 2->3, 3->4
        DiminishingReturnsExponent = 0.5m,
        ProductionRateBonusPerLevel = 0.1m,
        MaxCommitmentPerTurn = 1000m,
    };

    [Fact]
    public void CalculateResearchPoints_Is_Zero_For_No_Investment()
    {
        Assert.Equal(0m, RndCalculator.CalculateResearchPoints(0m, Config));
    }

    [Fact]
    public void CalculateResearchPoints_Applies_The_Diminishing_Returns_Exponent()
    {
        // 100^0.5 = 10.
        Assert.Equal(10m, RndCalculator.CalculateResearchPoints(100m, Config));
    }

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
        // 100 (1->2) и 400 (2->3) оба покрыты сразу вложением 400.
        Assert.Equal(3, RndCalculator.CalculateResultingLevel(currentLevel: 1, cumulativeInvestment: 400m, Config));
    }

    [Fact]
    public void CalculateResultingLevel_Stops_At_The_Highest_Configured_Level()
    {
        Assert.Equal(4, RndCalculator.CalculateResultingLevel(currentLevel: 1, cumulativeInvestment: 1_000_000m, Config));
    }

    [Fact]
    public void CalculateResultingLevel_A_Larger_Single_Payment_Yields_The_Same_Total_Progress_As_The_Same_Sum_Split_Over_Turns()
    {
        // Тот же приём и та же причина, что и у GenerationResearchCalculator (см. doc-comment
        // GenerationResearchCalculatorTests) — функция от НАКОПЛЕННОЙ суммы, не от суммы за ход, так
        // что дробление платежа не даёт эксплойта (не помогает и не вредит).
        var afterLumpSum = RndCalculator.CalculateResultingLevel(1, 100m, Config);
        var afterFourInstallments = RndCalculator.CalculateResultingLevel(1, 25m + 25m + 25m + 25m, Config);

        Assert.Equal(afterLumpSum, afterFourInstallments);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(4, true)] // 3 порога {10, 20, 30} -> уровни 1..4, 4 — максимальный
    [InlineData(5, true)] // выше максимального конфиг не предусматривает вовсе, но проверка всё равно должна отработать
    public void IsAtMaxLevel_Reflects_Whether_There_Is_A_Next_Threshold_Configured(int currentLevel, bool expected)
    {
        Assert.Equal(expected, RndCalculator.IsAtMaxLevel(currentLevel, Config));
    }
}
