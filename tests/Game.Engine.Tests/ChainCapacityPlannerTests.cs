using Game.Config;
using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.News;
using Game.Config.Session;

namespace Game.Engine.Tests;

/// <summary>
/// <see cref="ChainCapacityPlanner"/> — запрос пользователя 2026-09-07: дефицит сырья должен
/// закрываться теми же рычагами, что доступны живой команде (донайм, вторая фабрика на том же
/// уровне), а не переписыванием <c>ProductionRate</c> в конфиге. Проверяем, что план действительно
/// расшивает узкое место, делает это в правильном порядке (сначала дешёвый донайм, потом фабрика) и
/// не раздувает мощность там, где потребности нет.
/// </summary>
public class ChainCapacityPlannerTests
{
    /// <summary>Сырьё → передел, 2 единицы сырья на 1 единицу выхода; базовая численность 10, выпуск задаётся параметрами.</summary>
    private static ResolvedGameConfig BuildTwoLevelChain(decimal rawProductionRate, decimal processedProductionRate)
    {
        var config = new GameConfig
        {
            Sectors = [new SectorConfig { Id = "A", Name = "Сектор" }],
            Materials =
            [
                new MaterialConfig { Id = "raw", Name = "Сырьё", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "processed", Name = "Передел", SectorId = "A", Level = 1 },
            ],
            Recipes =
            [
                new RecipeConfig { Id = "raw-mining", OutputMaterialId = "raw", OutputQuantity = 1m, Inputs = [], ProductionRate = rawProductionRate },
                new RecipeConfig
                {
                    Id = "processing", OutputMaterialId = "processed", OutputQuantity = 1m,
                    Inputs = [new RecipeInputConfig { MaterialId = "raw", Quantity = 2m }], ProductionRate = processedProductionRate,
                },
            ],
            FactoryDefinitions =
            [
                new FactoryDefinitionConfig { Id = "mine", Name = "Рудник", SectorId = "A", RecipeIds = ["raw-mining"], BuildCost = 350m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 7m },
                new FactoryDefinitionConfig { Id = "plant", Name = "Завод", SectorId = "A", RecipeIds = ["processing"], BuildCost = 800m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 20m },
            ],
            StartingConditions = new StartingConditionsConfig { MaxInitialBuildBudget = 100_000m },
            Duration = new SessionDurationConfig { MinTurns = 5, MaxTurns = 5 },
            PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                BaseMarketPerMaterial =
                [
                    new MaterialMarketConfig { MaterialId = "raw", BasePrice = 1m, BaseCapacity = 1_000_000m },
                    new MaterialMarketConfig { MaterialId = "processed", BasePrice = 5m, BaseCapacity = 1_000_000m },
                ],
                MarketCapacityOverflowDiscount = 1m,
                ElectricityBasePrice = 2m,
                ElectricityConsumptionPerOutputUnit = 0.01m,
                TrendScenario = [],
                WarehouseLiquidationRate = 0.5m,
            },
            WorkerProductivity = new WorkerProductivityConfig
            {
                BaseWorkerCount = 10, DiminishingReturnsFactor = 0.5m,
                HireCostPerWorker = 25m, FireCostPerWorker = 30m, SalaryPerWorkerPerTurn = 3.33m,
            },
            Rnd = new RndConfig { ResearchPointThresholdsByLevel = [], DiminishingReturnsExponent = 0.5m, ProductionRateBonusPerLevel = 0.1m, MaxCommitmentPerTurn = 0m },
            Wear = new WearConfig
            {
                GracePeriodTurns = 1000, BaseWearRatePerTurn = 0.01m, AccelerationFactorPerTurn = 0.004m, MaxUpkeepPenaltyMultiplier = 0.5m,
                OverhaulTiers = [new OverhaulTierConfig { Id = "prevention", Name = "Профилактика", MinCondition = 0.9m, CostFraction = 0.02m, DurationTurns = 1, OutputMultiplier = 0.97m, SalaryRate = 1m, UpkeepRate = 1m }],
                CriticalConditionThreshold = 0.2m, ForcedRepairDurationTurns = 8, ForcedRepairSalaryRate = 0.66m, ForcedRepairUpkeepRate = 0.5m, PostForcedRepairCondition = 0.85m,
            },
            GenerationResearch = new GenerationResearchConfig { StartingGeneration = 1, ResearchPointThresholdsByGeneration = [25m], DiminishingReturnsExponent = 0.5m, MaxCommitmentPerTurn = 300m },
            Warehouse = new WarehouseConfig { FreeCapacity = 1_000_000m, OverageFeePerUnit = 0.1m },
            Reputation = new ReputationConfig { HalfLifeTurns = 10, WarmupTurns = 3, TerminationSeverityMultiplier = 3m },
            Contracts = new ContractsConfig { DeliveryMissPenaltyRate = 0.1m, TerminationPenaltyRate = 0.5m, VoluntaryTerminationFee = 100m, MaxActiveContractsPerTeam = null },
            Taxes = new TaxesConfig { PropertyTaxRatePerTurn = 0m, SalesTaxRate = 0m },
            News = [],
            FeatureFlags = new FeatureFlagsConfig { TaxesEnabled = false, EmergencyPurchaseEnabled = true },
        };

        return GameConfigLoader.Load(config);
    }

    [Fact]
    public void No_Expansion_When_The_Base_Config_Already_Covers_Demand()
    {
        // Передел просит 2 × 200 = 400/ход, сырьё даёт 500/ход — расширять нечего.
        var plan = ChainCapacityPlanner.Plan(BuildTwoLevelChain(rawProductionRate: 50m, processedProductionRate: 20m));

        var raw = plan.Values.Single(p => p.Level == 0);
        Assert.Equal(1, raw.FactoryCount);
        Assert.Equal(10, raw.WorkersPerFactory);
        Assert.Equal(400m, raw.DemandPerTurn);
        Assert.False(raw.IsUnsatisfiable);
    }

    [Fact]
    public void Deficit_Is_Closed_By_Hiring_Before_Building_A_Second_Factory()
    {
        // Сырьё 250/ход против спроса 400/ход (0.625×, как у coking-coal в metallurgy.json).
        // Нужна мощность ×1.6: эффективная мощность 10 → 16, то есть 22 рабочих
        // (линейно до 10, дальше с коэффициентом 0.5: 10 + 12×0.5 = 16). Второй фабрики быть не должно.
        var plan = ChainCapacityPlanner.Plan(BuildTwoLevelChain(rawProductionRate: 25m, processedProductionRate: 20m));

        var raw = plan.Values.Single(p => p.Level == 0);
        Assert.Equal(1, raw.FactoryCount);
        Assert.Equal(22, raw.WorkersPerFactory);
        Assert.True(raw.PlannedOutputPerTurn >= raw.DemandPerTurn);
        Assert.False(raw.IsUnsatisfiable);
    }

    [Fact]
    public void Second_Factory_Appears_Only_When_Hiring_Hits_Its_Ceiling()
    {
        // Сырьё 100/ход против спроса 400/ход (0.25×): потолок донайма — 30 рабочих, это
        // эффективная мощность 20, то есть максимум 200/ход с одной фабрики. Нужны две.
        var plan = ChainCapacityPlanner.Plan(BuildTwoLevelChain(rawProductionRate: 10m, processedProductionRate: 20m));

        var raw = plan.Values.Single(p => p.Level == 0);
        Assert.True(raw.FactoryCount >= 2, $"Ожидали вторую фабрику, план дал {raw.FactoryCount}.");
        Assert.True(raw.PlannedOutputPerTurn >= raw.DemandPerTurn);
        Assert.False(raw.IsUnsatisfiable);
    }

    [Fact]
    public void Unsatisfiable_Bottleneck_Is_Flagged_Instead_Of_Growing_Without_Limit()
    {
        // Разрыв в 40 раз: даже 4 фабрики по 30 рабочих (потолки планировщика) его не закрывают —
        // план обязан честно сказать «не расшивается», а не плодить фабрики бесконечно.
        var plan = ChainCapacityPlanner.Plan(BuildTwoLevelChain(rawProductionRate: 1m, processedProductionRate: 20m));

        var raw = plan.Values.Single(p => p.Level == 0);
        Assert.Equal(ChainCapacityPlanner.MaxFactoriesPerRecipe, raw.FactoryCount);
        Assert.Equal(10 * ChainCapacityPlanner.MaxWorkersMultiplier, raw.WorkersPerFactory);
        Assert.True(raw.IsUnsatisfiable);
    }

    [Fact]
    public void Final_Product_Without_Consumers_Stays_At_Base_Capacity()
    {
        var plan = ChainCapacityPlanner.Plan(BuildTwoLevelChain(rawProductionRate: 50m, processedProductionRate: 20m));

        var processed = plan.Values.Single(p => p.Level == 1);
        Assert.Equal(0m, processed.DemandPerTurn);
        Assert.Equal(1, processed.FactoryCount);
        Assert.Equal(10, processed.WorkersPerFactory);
    }
}
