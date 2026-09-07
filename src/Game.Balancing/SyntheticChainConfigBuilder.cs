using Game.Config;
using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.News;
using Game.Config.Session;

namespace Game.Balancing;

/// <summary>
/// Один сектор, N уровней друг на друге (каждый следующий потребляет фиксированное количество единиц
/// предыдущего) — тот же граф, что тестовый <c>ProductionCostLevelCalculatorTests.BuildThreeLevelChainConfig</c>,
/// но с произвольной глубиной и вынесен в продакшн-код: нужен направлению C плана исследований
/// (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>, 2026-08-24 — «BuildCost/ProductionRate
/// как параметризованные функции уровня, не таблица чисел»), где цепочка собирается заново на КАЖДУЮ
/// ячейку сетки (см. <see cref="GeometricChainSweep"/>), а не один раз из файла конфига. Остальные
/// секции конфига (Economy/WorkerProductivity/Rnd/...) — фиксированные разумные дефолты, они не
/// участвуют в направлении C (сравниваем только уровни между собой при фиксированной наценке).
/// </summary>
public static class SyntheticChainConfigBuilder
{
    /// <summary>Числа одного уровня цепочки — вход <see cref="Build"/>.</summary>
    public sealed record LevelParams(decimal BuildCost, decimal FixedCostPerTurn, decimal ProductionRate);

    /// <summary>
    /// Собирает N-уровневую цепочку (<paramref name="levels"/>[0] — сырьё без входов, каждый
    /// следующий уровень потребляет <paramref name="inputQuantityPerLevel"/> единиц предыдущего на
    /// 1 единицу своего выхода).
    /// </summary>
    public static ResolvedGameConfig Build(IReadOnlyList<LevelParams> levels, decimal inputQuantityPerLevel = 2m)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count == 0)
        {
            throw new ArgumentException("Нужен хотя бы один уровень.", nameof(levels));
        }

        var materials = new List<MaterialConfig>();
        var recipes = new List<RecipeConfig>();
        var factories = new List<FactoryDefinitionConfig>();
        var market = new List<MaterialMarketConfig>();

        for (var level = 0; level < levels.Count; level++)
        {
            var materialId = $"material{level}";
            var recipeId = $"recipe{level}";
            var factoryId = $"factory{level}";

            materials.Add(new MaterialConfig { Id = materialId, Name = $"Материал {level}", SectorId = "A", Level = level });
            recipes.Add(new RecipeConfig
            {
                Id = recipeId,
                OutputMaterialId = materialId,
                OutputQuantity = 1m,
                Inputs = level == 0 ? [] : [new RecipeInputConfig { MaterialId = $"material{level - 1}", Quantity = inputQuantityPerLevel }],
                ProductionRate = levels[level].ProductionRate,
            });
            factories.Add(new FactoryDefinitionConfig
            {
                Id = factoryId,
                Name = $"Фабрика {level}",
                SectorId = "A",
                RecipeIds = [recipeId],
                BuildCost = levels[level].BuildCost,
                LiquidationValueCoefficient = 0.5m,
                FixedCostPerTurn = levels[level].FixedCostPerTurn,
            });
            market.Add(new MaterialMarketConfig { MaterialId = materialId, BaseSellPrice = 10m, BaseCapacity = 1_000_000m });
        }

        var config = new GameConfig
        {
            Sectors = [new SectorConfig { Id = "A", Name = "Синтетический сектор" }],
            Materials = materials,
            Recipes = recipes,
            FactoryDefinitions = factories,
            StartingConditions = new StartingConditionsConfig { MaxInitialBuildBudget = 1_000_000m },
            Duration = new SessionDurationConfig { MinTurns = 5, MaxTurns = 90 },
            PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                BaseMarketPerMaterial = market,
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                ElectricityConsumptionPerOutputUnit = 0m,
                TrendScenario = [],
                WarehouseLiquidationRate = 0.5m,
            },
            WorkerProductivity = new WorkerProductivityConfig
            {
                BaseWorkerCount = 10,
                DiminishingReturnsFactor = 0.5m,
                HireCostPerWorker = 50m,
                FireCostPerWorker = 30m,
                SalaryPerWorkerPerTurn = 5m,
            },
            Rnd = new RndConfig { ResearchPointThresholdsByLevel = [100m], DiminishingReturnsExponent = 1m, ProductionRateBonusPerLevel = 0.1m, MaxCommitmentPerTurn = 200m },
            Wear = new WearConfig
            {
                GracePeriodTurns = 1000, BaseWearRatePerTurn = 0.01m, AccelerationFactorPerTurn = 0.004m, MaxUpkeepPenaltyMultiplier = 0.5m,
                OverhaulTiers = [new OverhaulTierConfig { Id = "prevention", Name = "Профилактика", MinCondition = 0.9m, CostFraction = 0.02m, DurationTurns = 1, OutputMultiplier = 0.97m, SalaryRate = 1m, UpkeepRate = 1m }],
                CriticalConditionThreshold = 0.2m, ForcedRepairDurationTurns = 8, ForcedRepairSalaryRate = 0.66m, ForcedRepairUpkeepRate = 0.5m, PostForcedRepairCondition = 0.85m,
            },
            GenerationResearch = new GenerationResearchConfig
            {
                StartingGeneration = levels.Count,
                ResearchPointThresholdsByGeneration = [],
                DiminishingReturnsExponent = 0.5m,
                MaxCommitmentPerTurn = 300m,
            },
            Warehouse = new WarehouseConfig { FreeCapacity = 1_000_000m, OverageFeePerUnit = 0.1m },
            Reputation = new ReputationConfig { HalfLifeTurns = 10, WarmupTurns = 3, TerminationSeverityMultiplier = 3m },
            Contracts = new ContractsConfig { DeliveryMissPenaltyRate = 0.1m, TerminationPenaltyRate = 0.5m, VoluntaryTerminationFee = 100m, MaxActiveContractsPerTeam = null },
            Taxes = new TaxesConfig { PropertyTaxRatePerTurn = 0m, SalesTaxRate = 0m },
            News = [],
            FeatureFlags = new FeatureFlagsConfig { TaxesEnabled = false, EmergencyPurchaseEnabled = true },
        };

        return GameConfigLoader.Load(config);
    }
}
