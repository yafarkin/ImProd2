using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.ProductionModel;
using Game.Config.Session;

namespace Game.Config.Tests;

public class GameConfigComposerTests
{
    private static ProductionModelConfig BuildProductionModel() => new()
    {
        Sectors = new[] { new SectorConfig { Id = "A", Name = "Металлургия" } },
        Materials = new[] { new MaterialConfig { Id = "ore", Name = "Руда", SectorId = "A", Level = 0 } },
        Recipes = new[]
        {
            new RecipeConfig
            {
                Id = "ore-mining",
                OutputMaterialId = "ore",
                OutputQuantity = 1m,
                Inputs = Array.Empty<RecipeInputConfig>(),
                ProductionRate = 1m,
            },
        },
        FactoryDefinitions = new[]
        {
            new FactoryDefinitionConfig
            {
                Id = "mine", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" },
                BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m,
            },
        },
        BaseMarketPerMaterial = new[] { new MaterialMarketConfig { MaterialId = "ore", BasePrice = 3m, BaseCapacity = 500m } },
        GenerationResearch = new GenerationResearchConfig
        {
            StartingGeneration = 1,
            ResearchPointThresholdsByGeneration = new[] { 100m },
            DiminishingReturnsExponent = 0.5m,
            MaxCommitmentPerTurn = 100m,
        },
    };

    private static SessionConfig BuildSession() => new()
    {
        StartingConditions = new StartingConditionsConfig
        {
            MaxInitialBuildBudget = 1000m,
        },
        SessionPresets = new[] { new SessionPresetConfig { Id = "short", Name = "Short", MinTurns = 1, MaxTurns = 2, TurnDurationMinutes = 1 } },
        PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
        Economy = new SessionEconomyConfig
        {
            EmergencyPurchaseBaseMultiplier = 1.5m,
            EmergencyPurchasePressureMultiplierPerUnit = 0.1m,
            EmergencyPurchasePressureHalfLifeTurns = 5,
            MarginMultiplierByProcessingLevel = Array.Empty<ProcessingLevelMarginConfig>(),
            MarketCapacityOverflowDiscount = 0.5m,
            ElectricityBasePrice = 0.2m,
            ElectricityConsumptionPerOutputUnit = 0.1m,
            TrendScenario = Array.Empty<EconomyTrendPhaseConfig>(),
            WarehouseLiquidationRate = 0.5m,
        },
        WorkerProductivity = new WorkerProductivityConfig
        {
            BaseWorkerCount = 10,
            DiminishingReturnsFactor = 0.5m,
            HireCostPerWorker = 50m,
            FireCostPerWorker = 20m,
            SalaryPerWorkerPerTurn = 5m,
            TeamSalaryBaseWorkerCount = 30,
            SalaryEscalationFactor = 0.01m,
        },
        Rnd = new RndConfig
        {
            ResearchPointThresholdsByLevel = Array.Empty<decimal>(),
            DiminishingReturnsExponent = 0.5m,
            ProductionRateBonusPerLevel = 0.1m,
            MaxCommitmentPerTurn = 300m,
        },
        Wear = new WearConfig
        {
            GracePeriodTurns = 5,
            BaseWearRatePerTurn = 0.01m,
            AccelerationFactorPerTurn = 0.001m,
            MaxUpkeepPenaltyMultiplier = 2m,
            OverhaulTiers = Array.Empty<OverhaulTierConfig>(),
            CriticalConditionThreshold = 0.2m,
            ForcedRepairDurationTurns = 2,
            ForcedRepairSalaryRate = 0.5m,
            ForcedRepairUpkeepRate = 0.5m,
            PostForcedRepairCondition = 0.85m,
        },
        Warehouse = new WarehouseConfig { FreeCapacity = 1000m, OverageFeePerUnit = 0.1m },
        Reputation = new ReputationConfig { HalfLifeTurns = 10, WarmupTurns = 3, TerminationSeverityMultiplier = 3m },
        Contracts = new ContractsConfig
        {
            DeliveryMissPenaltyRate = 0.1m,
            TerminationPenaltyRate = 0.5m,
            VoluntaryTerminationFee = 100m,
            MaxActiveContractsPerTeam = null,
        },
        Taxes = new TaxesConfig { PropertyTaxRatePerTurn = 0m, SalesTaxRate = 0m },
        Deposits = new DepositsConfig { InterestRatePerTurn = 0m },
        News = Array.Empty<Config.News.NewsItemConfig>(),
        FeatureFlags = new FeatureFlagsConfig { TaxesEnabled = false, DepositsEnabled = false, EmergencyPurchaseEnabled = true },
    };

    [Fact]
    public void Compose_Takes_Catalog_And_GenerationResearch_From_The_Production_Model()
    {
        var config = GameConfigComposer.Compose(BuildProductionModel(), BuildSession());

        Assert.Equal("A", config.Sectors.Single().Id);
        Assert.Equal("ore", config.Materials.Single().Id);
        Assert.Equal("ore-mining", config.Recipes.Single().Id);
        Assert.Equal("mine", config.FactoryDefinitions.Single().Id);
        Assert.Equal(1, config.GenerationResearch.StartingGeneration);
    }

    [Fact]
    public void Compose_Takes_BaseMarketPerMaterial_From_The_Model_And_The_Rest_Of_Economy_From_The_Session()
    {
        var config = GameConfigComposer.Compose(BuildProductionModel(), BuildSession());

        var orePrice = Assert.Single(config.Economy.BaseMarketPerMaterial);
        Assert.Equal("ore", orePrice.MaterialId);
        Assert.Equal(3m, orePrice.BasePrice);

        Assert.Equal(1.5m, config.Economy.EmergencyPurchaseBaseMultiplier);
        Assert.Equal(0.5m, config.Economy.WarehouseLiquidationRate);
    }

    [Fact]
    public void Compose_Result_Passes_Full_Validation()
    {
        var config = GameConfigComposer.Compose(BuildProductionModel(), BuildSession());

        var errors = GameConfigValidator.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void Compose_Applies_DifficultyScaler_Using_The_Session_DifficultyLevel()
    {
        var session = BuildSession() with { DifficultyLevel = 0.0 };

        var config = GameConfigComposer.Compose(BuildProductionModel(), session);

        // BuildCost-анкер уровня 0 — множитель 0.5 (docs/difficulty.md §3): 100m -> 50m.
        Assert.Equal(50m, config.FactoryDefinitions.Single().BuildCost);
    }

    [Fact]
    public void Compose_Leaves_Values_Unchanged_At_The_Default_DifficultyLevel()
    {
        var config = GameConfigComposer.Compose(BuildProductionModel(), BuildSession());

        // BuildSession() не задаёт DifficultyLevel — дефолт 3.0, нейтральный уровень.
        Assert.Equal(100m, config.FactoryDefinitions.Single().BuildCost);
    }
}
