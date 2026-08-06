using Game.Config.Economy;

namespace Game.Engine.Tests;

public class GenerationResearchCalculatorTests
{
    private static readonly GenerationResearchConfig Config = new()
    {
        StartingGeneration = 1,
        // sqrt(100)=10, sqrt(400)=20, sqrt(900)=30 — накопленные ¤: 100, 400, 900.
        ResearchPointThresholdsByGeneration = new[] { 10m, 20m, 30m },
        DiminishingReturnsExponent = 0.5m,
        MaxCommitmentPerTurn = 1000m,
    };

    [Fact]
    public void CalculateResearchPoints_Is_Zero_For_No_Investment()
    {
        Assert.Equal(0m, GenerationResearchCalculator.CalculateResearchPoints(0m, Config));
    }

    [Fact]
    public void CalculateResearchPoints_Applies_The_Diminishing_Returns_Exponent()
    {
        // 100^0.5 = 10.
        Assert.Equal(10m, GenerationResearchCalculator.CalculateResearchPoints(100m, Config));
    }

    [Fact]
    public void CalculateResultingGeneration_Stays_At_Current_Generation_Below_The_Next_Threshold()
    {
        Assert.Equal(1, GenerationResearchCalculator.CalculateResultingGeneration(currentGeneration: 1, cumulativeInvestment: 99m, Config));
    }

    [Fact]
    public void CalculateResultingGeneration_Advances_One_Generation_Exactly_At_The_Threshold()
    {
        Assert.Equal(2, GenerationResearchCalculator.CalculateResultingGeneration(currentGeneration: 1, cumulativeInvestment: 100m, Config));
    }

    [Fact]
    public void CalculateResultingGeneration_Advances_Multiple_Generations_At_Once_For_A_Large_Investment()
    {
        // 100 (1->2) и 400 (2->3) оба покрыты сразу вложением 400.
        Assert.Equal(3, GenerationResearchCalculator.CalculateResultingGeneration(currentGeneration: 1, cumulativeInvestment: 400m, Config));
    }

    [Fact]
    public void CalculateResultingGeneration_Stops_At_The_Highest_Configured_Generation()
    {
        Assert.Equal(4, GenerationResearchCalculator.CalculateResultingGeneration(currentGeneration: 1, cumulativeInvestment: 1_000_000m, Config));
    }

    [Fact]
    public void CalculateResultingGeneration_A_Larger_Single_Payment_Yields_Less_Total_Progress_Than_The_Same_Sum_Split_Over_Turns()
    {
        // Запрос пользователя: закинуть всё сразу должно быть менее эффективно, чем растянуть по
        // ходам — 100 разом даёт очки 10 (ровно первый порог), а 4 вложения по 25 подряд (то же
        // накопленное 100 к последнему) считаются от той же накопленной суммы, так что при этой
        // формуле (функция от НАКОПЛЕННОЙ суммы) результат идентичен — дробление не помогает и не
        // вредит, что и требовалось (без эксплойта, см. doc-comment GenerationResearchConfig).
        var afterLumpSum = GenerationResearchCalculator.CalculateResultingGeneration(1, 100m, Config);
        var afterFourInstallments = GenerationResearchCalculator.CalculateResultingGeneration(1, 25m + 25m + 25m + 25m, Config);

        Assert.Equal(afterLumpSum, afterFourInstallments);
    }
}
