using System.Text.Json;
using Game.Config.Economy;
using Game.Config.Session;

namespace Game.Config.Tests;

public class GameConfigDeserializationTests
{
    private static GameConfig LoadSampleConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<GameConfig>(json)
               ?? throw new InvalidOperationException("Sample config deserialized to null.");
    }

    private static SessionConfig LoadDebugSessionConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "debug.json");
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<SessionConfig>(json)
               ?? throw new InvalidOperationException("Debug session config deserialized to null.");
    }

    [Fact]
    public void Sample_Config_Deserializes_Catalog_Without_Loss()
    {
        var config = LoadSampleConfig();

        Assert.Equal(2, config.Sectors.Count);
        Assert.Contains(config.Sectors, sector => sector is { Id: "A", Name: "Металлургия" });

        Assert.Equal(5, config.Materials.Count);
        var ore = Assert.Single(config.Materials, material => material.Id == "ore");
        Assert.Equal("A", ore.SectorId);
        Assert.Equal(0, ore.Level);

        Assert.Equal(5, config.Recipes.Count);
        var rebarRecipe = Assert.Single(config.Recipes, recipe => recipe.Id == "rebar-from-sheet");
        Assert.Equal("rebar", rebarRecipe.OutputMaterialId);
        Assert.Equal(10m, rebarRecipe.OutputQuantity);
        var rebarInput = Assert.Single(rebarRecipe.Inputs);
        Assert.Equal("sheet", rebarInput.MaterialId);
        Assert.Equal(3m, rebarInput.Quantity);

        var oreMiningRecipe = Assert.Single(config.Recipes, recipe => recipe.Id == "ore-mining");
        Assert.Equal("ore", oreMiningRecipe.OutputMaterialId);
        Assert.Empty(oreMiningRecipe.Inputs); // сырьё добывается, а не строится из других материалов

        Assert.Equal(5, config.FactoryDefinitions.Count);
        var steelMill = Assert.Single(config.FactoryDefinitions, factory => factory.Id == "steel-mill");
        Assert.Equal("A", steelMill.SectorId);
        Assert.Equal(new[] { "sheet-from-ore" }, steelMill.RecipeIds);
        Assert.Equal(1500m, steelMill.BuildCost);
        Assert.Equal(0.5m, steelMill.LiquidationValueCoefficient);
    }

    [Fact]
    public void Sample_Config_Deserializes_Starting_Conditions_And_Session_Presets_Without_Loss()
    {
        var config = LoadSampleConfig();

        Assert.Equal(10000m, config.StartingConditions.MaxInitialBuildBudget);

        Assert.Equal(3, config.SessionPresets.Count);
        var shortPreset = Assert.Single(config.SessionPresets, preset => preset.Id == "short");
        Assert.Equal(15, shortPreset.MinTurns);
        Assert.Equal(20, shortPreset.MaxTurns);

        Assert.Equal(20, config.PhaseTiming.SettlementPhaseSeconds);
        Assert.Equal(300, config.PhaseTiming.DecisionPhaseSeconds);
    }

    /// <summary>Отладочные сессионные параметры (кнопка «Отладочный конфиг» на /admin) — 30-секундный ход и 300 ходов подряд, чтобы наблюдать динамику без ожидания реальной игры; сочетаются с любой производственной моделью.</summary>
    [Fact]
    public void Debug_Session_Config_Has_A_Thirty_Second_Turn_Cycle_And_Three_Hundred_Turns()
    {
        var session = LoadDebugSessionConfig();

        Assert.Equal(5, session.PhaseTiming.SettlementPhaseSeconds);
        Assert.Equal(25, session.PhaseTiming.DecisionPhaseSeconds);

        var preset = Assert.Single(session.SessionPresets);
        Assert.Equal("debug", preset.Id);
        Assert.Equal(300, preset.MinTurns);
        Assert.Equal(300, preset.MaxTurns);
    }

    [Fact]
    public void Sample_Config_Deserializes_Economy_And_Trend_Scenario_Without_Loss()
    {
        var config = LoadSampleConfig();

        Assert.Equal(1.5m, config.Economy.EmergencyPurchaseBaseMultiplier);
        Assert.Equal(9, config.Economy.MarginMultiplierByProcessingLevel.Count);
        Assert.Equal(0.5m, config.Economy.MarketCapacityOverflowDiscount);
        Assert.Equal(0.5m, config.Economy.WarehouseLiquidationRate);

        Assert.Equal(5, config.Economy.BaseMarketPerMaterial.Count);
        var orePrice = Assert.Single(config.Economy.BaseMarketPerMaterial, p => p.MaterialId == "ore");
        Assert.Equal(1m, orePrice.BasePrice);
        Assert.Equal(200m, orePrice.BaseCapacity);

        Assert.Equal(3, config.Economy.TrendScenario.Count);
        var upPhase = Assert.Single(config.Economy.TrendScenario, phase => phase.Trend == EconomyTrend.Up);
        Assert.Equal(11, upPhase.StartTurn);
        Assert.Equal(25, upPhase.EndTurn);
    }

    [Fact]
    public void Sample_Config_Deserializes_Balancing_Sections_Without_Loss()
    {
        var config = LoadSampleConfig();

        Assert.Equal(10, config.WorkerProductivity.BaseWorkerCount);
        Assert.Equal(5m, config.WorkerProductivity.SalaryPerWorkerPerTurn);
        Assert.Equal(new[] { 32m, 55m, 77m }, config.Rnd.ResearchPointThresholdsByLevel);
        Assert.Equal(0.5m, config.Rnd.DiminishingReturnsExponent);
        Assert.Equal(0.1m, config.Rnd.ProductionRateBonusPerLevel);
        Assert.Equal(500m, config.Warehouse.FreeCapacity);
        Assert.Equal(10, config.Reputation.HalfLifeTurns);
        Assert.Equal(3, config.Reputation.WarmupTurns);
        Assert.Equal(3m, config.Reputation.TerminationSeverityMultiplier);

        Assert.Equal(0.1m, config.Contracts.DeliveryMissPenaltyRate);
        Assert.Equal(0.5m, config.Contracts.TerminationPenaltyRate);
        Assert.Null(config.Contracts.MaxActiveContractsPerTeam);

        Assert.Equal(0.01m, config.Taxes.PropertyTaxRatePerTurn);
        Assert.Equal(0.01m, config.Deposits.InterestRatePerTurn);
    }

    [Fact]
    public void Sample_Config_Deserializes_News_And_Feature_Flags_Without_Loss()
    {
        var config = LoadSampleConfig();

        Assert.Equal(4, config.News.Count);
        Assert.Equal(2, config.News.Count(item => item.Trend == EconomyTrend.Down));

        Assert.False(config.FeatureFlags.TaxesEnabled);
        Assert.False(config.FeatureFlags.DepositsEnabled);
        Assert.True(config.FeatureFlags.EmergencyPurchaseEnabled);
    }
}
