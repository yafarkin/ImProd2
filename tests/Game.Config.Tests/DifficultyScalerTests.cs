using Game.Config.Catalog;
using Game.Config.Economy;

namespace Game.Config.Tests;

/// <summary>
/// Интерполяция и применение семи рычагов сложности (<c>docs/difficulty.md</c>, было восемь — рычаг
/// ставки по займу убран вместе с банковским займом как классом механики, docs/TODO.md #23) —
/// <see cref="DifficultyScaler"/>, шаг 2 плана реализации из этого документа.
/// </summary>
public class DifficultyScalerTests
{
    private static GameConfig BuildConfig()
    {
        var config = GameConfigTestBuilder.Build(
            factoryDefinitions: new[]
            {
                new FactoryDefinitionConfig
                {
                    Id = "mine", Name = "Рудник", SectorId = "A", RecipeIds = Array.Empty<string>(),
                    BuildCost = 1000m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m,
                },
            });

        return config with
        {
            Economy = config.Economy with
            {
                BaseMarketPerMaterial = new[] { new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 100m } },
            },
            GenerationResearch = config.GenerationResearch with
            {
                ResearchPointThresholdsByGeneration = new[] { 500m },
            },
        };
    }

    [Fact]
    public void Apply_At_Level_Three_Leaves_The_Config_Unchanged()
    {
        var config = BuildConfig();

        var scaled = DifficultyScaler.Apply(config, 3.0);

        Assert.Equivalent(config, scaled, strict: true);
    }

    [Fact]
    public void Apply_Interpolates_Linearly_Between_The_First_Two_Anchors()
    {
        var config = BuildConfig();

        // BuildCost-анкеры уровней 0/1 — 0.5/0.7 (docs/difficulty.md §3), на уровне 0.5 — ровно
        // среднее, 0.6.
        var scaled = DifficultyScaler.Apply(config, 0.5);

        Assert.Equal(600m, scaled.FactoryDefinitions.Single().BuildCost, precision: 6);
    }

    [Fact]
    public void Apply_Interpolates_Linearly_Between_The_Last_Two_Anchors()
    {
        var config = BuildConfig();

        // BuildCost-анкеры уровней 4/5 — 1.3/1.7, на уровне 4.7 (вес 0.7 к пятому) — 1.3 + 0.4*0.7 = 1.58.
        var scaled = DifficultyScaler.Apply(config, 4.7);

        Assert.Equal(1580m, scaled.FactoryDefinitions.Single().BuildCost, precision: 3);
    }

    [Fact]
    public void Apply_Clamps_Levels_Below_Zero_To_The_Zero_Anchor()
    {
        var config = BuildConfig();

        var atMinusOne = DifficultyScaler.Apply(config, -1.0);
        var atZero = DifficultyScaler.Apply(config, 0.0);

        Assert.Equal(atZero.FactoryDefinitions.Single().BuildCost, atMinusOne.FactoryDefinitions.Single().BuildCost);
    }

    [Fact]
    public void Apply_Clamps_Levels_Above_Five_To_The_Five_Anchor()
    {
        var config = BuildConfig();

        var atSeven = DifficultyScaler.Apply(config, 7.0);
        var atFive = DifficultyScaler.Apply(config, 5.0);

        Assert.Equal(atFive.FactoryDefinitions.Single().BuildCost, atSeven.FactoryDefinitions.Single().BuildCost);
    }

    [Fact]
    public void Apply_At_Level_Zero_Moves_All_Seven_Levers_In_The_Easier_Direction()
    {
        var config = BuildConfig();

        var scaled = DifficultyScaler.Apply(config, 0.0);

        Assert.True(scaled.FactoryDefinitions.Single().BuildCost < config.FactoryDefinitions.Single().BuildCost);
        Assert.True(scaled.WorkerProductivity.SalaryEscalationFactor < config.WorkerProductivity.SalaryEscalationFactor);
        Assert.True(scaled.Rnd.ProductionRateBonusPerLevel > config.Rnd.ProductionRateBonusPerLevel);
        Assert.True(scaled.Rnd.ResearchPointThresholdsByLevel[0] < config.Rnd.ResearchPointThresholdsByLevel[0]);
        Assert.True(scaled.GenerationResearch.ResearchPointThresholdsByGeneration[0] < config.GenerationResearch.ResearchPointThresholdsByGeneration[0]);
        Assert.True(scaled.Economy.BaseMarketPerMaterial.Single().BasePrice > config.Economy.BaseMarketPerMaterial.Single().BasePrice);
        Assert.True(scaled.Economy.EmergencyPurchaseBaseMultiplier < config.Economy.EmergencyPurchaseBaseMultiplier);
        Assert.True(scaled.Wear.AccelerationFactorPerTurn < config.Wear.AccelerationFactorPerTurn);
    }

    [Fact]
    public void Apply_At_Level_Five_Moves_All_Seven_Levers_In_The_Harder_Direction()
    {
        var config = BuildConfig();

        var scaled = DifficultyScaler.Apply(config, 5.0);

        Assert.True(scaled.FactoryDefinitions.Single().BuildCost > config.FactoryDefinitions.Single().BuildCost);
        Assert.True(scaled.WorkerProductivity.SalaryEscalationFactor > config.WorkerProductivity.SalaryEscalationFactor);
        Assert.True(scaled.Rnd.ProductionRateBonusPerLevel < config.Rnd.ProductionRateBonusPerLevel);
        Assert.True(scaled.Rnd.ResearchPointThresholdsByLevel[0] > config.Rnd.ResearchPointThresholdsByLevel[0]);
        Assert.True(scaled.GenerationResearch.ResearchPointThresholdsByGeneration[0] > config.GenerationResearch.ResearchPointThresholdsByGeneration[0]);
        Assert.True(scaled.Economy.BaseMarketPerMaterial.Single().BasePrice < config.Economy.BaseMarketPerMaterial.Single().BasePrice);
        Assert.True(scaled.Economy.EmergencyPurchaseBaseMultiplier > config.Economy.EmergencyPurchaseBaseMultiplier);
        Assert.True(scaled.Wear.AccelerationFactorPerTurn > config.Wear.AccelerationFactorPerTurn);
    }
}
