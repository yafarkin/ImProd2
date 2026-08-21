using Game.Config;
using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.News;
using Game.Config.Session;

namespace Game.Bots.Llm.Tests;

/// <summary>
/// Минимальный валидный двухсекторный конфиг с настоящей межсекторной зависимостью (сектор Б ест
/// материал сектора А, обратной зависимости нет) — только для <see cref="BotStateSnapshotBuilderTests"/>,
/// проверяющих <c>AppendCrossSectorDemand</c>. Тот же приём, что и
/// <c>Game.Bots.Tests.CrossSectorTradingTests.BuildTwoSectorConfig</c> (заведён независимо, не
/// переиспользован оттуда — LLM-слой сознательно не ссылается на <c>Game.Bots</c>, см. doc-comment
/// плана шага 1), но без стакана/ботов: тут не нужно доигрывать сессию, только собрать снимок.
/// </summary>
internal static class CrossSectorConfigBuilder
{
    public static ResolvedGameConfig Build()
    {
        var config = new GameConfig
        {
            Sectors =
            [
                new SectorConfig { Id = "A", Name = "Металлургия" },
                new SectorConfig { Id = "B", Name = "Нефтегазохимия" },
            ],
            Materials =
            [
                new MaterialConfig { Id = "ore", Name = "Руда", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "a-part", Name = "Деталь А", SectorId = "A", Level = 1 },
                new MaterialConfig { Id = "oil", Name = "Нефть", SectorId = "B", Level = 0 },
                new MaterialConfig { Id = "b-widget", Name = "Изделие Б", SectorId = "B", Level = 1 },
            ],
            Recipes =
            [
                new RecipeConfig { Id = "ore-mining", OutputMaterialId = "ore", OutputQuantity = 1m, Inputs = [], ProductionRate = 1m },
                new RecipeConfig
                {
                    Id = "a-part-from-ore", OutputMaterialId = "a-part", OutputQuantity = 1m,
                    Inputs = [new RecipeInputConfig { MaterialId = "ore", Quantity = 2m }], ProductionRate = 1m,
                },
                new RecipeConfig { Id = "oil-drilling", OutputMaterialId = "oil", OutputQuantity = 1m, Inputs = [], ProductionRate = 1m },
                new RecipeConfig
                {
                    // Б зависит от А (a-part), А от Б — нет; асимметрично специально, чтобы тест видел
                    // разный текст с обеих сторон (см. Build_TwoSectorsWithARealCrossDependency...).
                    Id = "b-widget-from-oil-and-a-part", OutputMaterialId = "b-widget", OutputQuantity = 1m,
                    Inputs =
                    [
                        new RecipeInputConfig { MaterialId = "oil", Quantity = 2m },
                        new RecipeInputConfig { MaterialId = "a-part", Quantity = 1m },
                    ],
                    ProductionRate = 1m,
                },
            ],
            FactoryDefinitions =
            [
                new FactoryDefinitionConfig { Id = "mine-a", Name = "Рудник", SectorId = "A", RecipeIds = ["ore-mining"], BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "plant-a", Name = "Завод А", SectorId = "A", RecipeIds = ["a-part-from-ore"], BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "well-b", Name = "Скважина", SectorId = "B", RecipeIds = ["oil-drilling"], BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "plant-b", Name = "Завод Б", SectorId = "B", RecipeIds = ["b-widget-from-oil-and-a-part"], BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
            ],
            StartingConditions = new StartingConditionsConfig
            {
                MaxInitialBuildBudget = 100_000m,
            },
            SessionPresets = [new SessionPresetConfig { Id = "short", Name = "Короткая", MinTurns = 15, MaxTurns = 15, TurnDurationMinutes = 1 }],
            PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                BaseMarketPerMaterial =
                [
                    new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "a-part", BasePrice = 23m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "oil", BasePrice = 10m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "b-widget", BasePrice = 40m, BaseCapacity = 100_000m },
                ],
                MarginMultiplierByProcessingLevel = [new ProcessingLevelMarginConfig { Level = 1, MarginMultiplier = 1.2m }],
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                ElectricityConsumptionPerOutputUnit = 0m,
                TrendScenario = [],
                WarehouseLiquidationRate = 0.5m,
            },
            WorkerProductivity = new WorkerProductivityConfig
            {
                BaseWorkerCount = 5,
                DiminishingReturnsFactor = 0.5m,
                HireCostPerWorker = 50m,
                FireCostPerWorker = 30m,
                SalaryPerWorkerPerTurn = 5m,
                TeamSalaryBaseWorkerCount = 1000,
                SalaryEscalationFactor = 1.5m,
            },
            Rnd = new RndConfig
            {
                ResearchPointThresholdsByLevel = [100m, 300m],
                DiminishingReturnsExponent = 1m,
                ProductionRateBonusPerLevel = 0.1m,
                MaxCommitmentPerTurn = 200m,
            },
            Wear = new WearConfig
            {
                GracePeriodTurns = 1000,
                BaseWearRatePerTurn = 0.01m,
                AccelerationFactorPerTurn = 0.004m,
                MaxUpkeepPenaltyMultiplier = 0.5m,
                OverhaulTiers = [new OverhaulTierConfig { Id = "prevention", Name = "Профилактика", MinCondition = 0.9m, CostFraction = 0.02m, DurationTurns = 1, OutputMultiplier = 0.97m, SalaryRate = 1m, UpkeepRate = 1m }],
                CriticalConditionThreshold = 0.2m,
                ForcedRepairDurationTurns = 8,
                ForcedRepairSalaryRate = 0.66m,
                ForcedRepairUpkeepRate = 0.5m,
                PostForcedRepairCondition = 0.85m,
            },
            GenerationResearch = new GenerationResearchConfig
            {
                StartingGeneration = 1,
                ResearchPointThresholdsByGeneration = [],
                DiminishingReturnsExponent = 0.5m,
                MaxCommitmentPerTurn = 300m,
            },
            Warehouse = new WarehouseConfig { FreeCapacity = 1_000_000m, OverageFeePerUnit = 0.1m },
            Reputation = new ReputationConfig { HalfLifeTurns = 10, WarmupTurns = 3, TerminationSeverityMultiplier = 3m },
            Contracts = new ContractsConfig
            {
                DeliveryMissPenaltyRate = 0.1m,
                TerminationPenaltyRate = 0.5m,
                VoluntaryTerminationFee = 100m,
                MaxActiveContractsPerTeam = null,
            },
            Taxes = new TaxesConfig { PropertyTaxRatePerTurn = 0m, SalesTaxRate = 0m },
            News = [],
            FeatureFlags = new FeatureFlagsConfig
            {
                TaxesEnabled = false,
                EmergencyPurchaseEnabled = true,
            },
        };

        return GameConfigLoader.Load(config);
    }
}
