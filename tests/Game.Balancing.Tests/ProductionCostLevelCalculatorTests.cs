using Game.Config;
using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.News;
using Game.Config.Session;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing.Tests;

/// <summary>
/// <see cref="ProductionCostLevelCalculator.FactoryRecipeCost.PaybackTurns"/> — направление A плана
/// исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>, 2026-08-23): окупаемость
/// каждого уровня в изоляции, без хода/бота/кросс-торговли, при продаже 100% выпуска системе по
/// фиксированной наценке.
/// </summary>
public class ProductionCostLevelCalculatorTests
{
    [Fact]
    public void PaybackTurns_Matches_BuildCost_Divided_By_Profit_Per_Turn_At_System_Sale_Margin()
    {
        // BuildCost=1000, workers=1 (в линейной зоне отдачи, ЭффективнаяМощность=workers ровно) =>
        // Выпуск=ProductionRate×1=100, FixedCostPerTurn=30, без входов/электричества =>
        // Себестоимость=30/100=0.3 (зарплата в неё намеренно не входит, см. doc-comment класса).
        // Прибыль считается от собственного передела (docs/economy-accounting-audit.md, дефект 3):
        // передел = FixedCostPerTurn 30 + электричество 0 + зарплата 1×5 = 35,
        // прибыль/ход = 35 × (1.30-1) = 10.5, окупаемость = 1000/10.5.
        var config = BuildSingleFactoryConfig(buildCost: 1000m, fixedCostPerTurn: 30m, productionRate: 100m);

        var rows = ProductionCostLevelCalculator.Calculate(config, workersPerFactory: 1);
        var row = rows.Single();

        Assert.Equal(0.3m, row.UnitCost);
        Assert.Equal(35m, row.ConversionCost);
        Assert.Equal(10.5m, row.ProfitPerTurn);
        Assert.Equal(1000m / 10.5m, row.PaybackTurns);
    }

    [Fact]
    public void PaybackTurns_Is_Null_When_The_Factory_Has_No_Recurring_Cost_Basis()
    {
        // FixedCostPerTurn=0, без входов, без электричества, зарплата 0 => собственный передел = 0
        // => Прибыль/ход=0 (наценка 30% от нуля — тоже ноль) — формула честно возвращает null, не
        // бесконечность и не ноль ходов: делить BuildCost не на что.
        var config = BuildSingleFactoryConfig(buildCost: 1000m, fixedCostPerTurn: 0m, productionRate: 100m, salaryPerWorkerPerTurn: 0m);

        var rows = ProductionCostLevelCalculator.Calculate(config, workersPerFactory: 1);
        var row = rows.Single();

        Assert.Equal(0m, row.UnitCost);
        Assert.Null(row.PaybackTurns);
    }

    /// <summary>
    /// Доказывает, что метрика реально ловит «уровень стоит непропорционально дорого относительно
    /// того, что он производит», а не просто растёт вместе с глубиной цепочки линейно (что само по
    /// себе не проблема — фабрики глубже обычно и дороже, и мощнее разом). Здесь BuildCost растёт
    /// на порядок за уровень, а FixedCostPerTurn/ProductionRate — нет: окупаемость должна взрываться,
    /// не просто расти. <b>Не</b> буквальное воспроизведение находки 2026-08-23 (там резкий обвал
    /// X(t) при разблокировке уровней 4-6 оказался, по всей видимости, связан с расходами на R&amp;D/
    /// исследование поколений — а они намеренно не входят в эту статическую метрику, см. doc-comment
    /// класса, «зарплата... R&amp;D и капремонт... не варьируются по сектору») — здесь проверяется
    /// сама механика метрики на явно патологическом случае, не тот конкретный инцидент.
    /// </summary>
    [Fact]
    public void PaybackTurns_Blows_Up_When_BuildCost_Outgrows_What_The_Level_Actually_Produces()
    {
        var config = BuildThreeLevelChainConfig(
            buildCosts: [500m, 5000m, 50000m],
            fixedCosts: [6m, 14.4m, 30m],
            productionRates: [50m, 20m, 8m]);

        var rows = ProductionCostLevelCalculator.Calculate(config, workersPerFactory: 10)
            .OrderBy(r => r.Level)
            .ToList();

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.PaybackTurns));

        var paybackByLevel = rows.Select(r => r.PaybackTurns!.Value).ToList();
        Assert.True(paybackByLevel[1] > paybackByLevel[0], "Окупаемость уровня 1 должна быть дольше уровня 0.");
        Assert.True(paybackByLevel[2] > paybackByLevel[1], "Окупаемость уровня 2 должна быть дольше уровня 1.");

        // Не просто "дольше", а на порядок — это и есть сигнал "уровень экономически не тянет",
        // не постепенное усложнение.
        Assert.True(paybackByLevel[2] > paybackByLevel[0] * 5m,
            $"Ожидали взрывной, не линейный рост окупаемости: уровень 0={paybackByLevel[0]:F1}, уровень 2={paybackByLevel[2]:F1}.");
    }

    /// <summary>
    /// Направление B плана исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>,
    /// 2026-08-24) — обратная задача: по целевому сроку окупаемости найти допустимый BuildCost, без
    /// бисекции (прямая формула — <see cref="ProductionCostLevelCalculator.FactoryRecipeCost.ProfitPerTurn"/>
    /// от BuildCost не зависит). Проверяем и что формула верна, и что она самосогласована с уже
    /// существующим <see cref="ProductionCostLevelCalculator.FactoryRecipeCost.PaybackTurns"/>: если
    /// взять сам текущий BuildCost как цель, получим ровно его обратно.
    /// </summary>
    [Fact]
    public void MaxBuildCostForTargetPayback_Is_ProfitPerTurn_Times_Target_And_Round_Trips_With_PaybackTurns()
    {
        // Тот же конфиг, что у первого теста: Прибыль/ход = 10.5.
        var config = BuildSingleFactoryConfig(buildCost: 1000m, fixedCostPerTurn: 30m, productionRate: 100m);

        var rows = ProductionCostLevelCalculator.Calculate(config, workersPerFactory: 1);
        var row = rows.Single();

        Assert.Equal(10.5m, row.ProfitPerTurn);
        Assert.Equal(105m, row.MaxBuildCostForTargetPayback(10m));

        // Обратный проход: если взять сам текущий срок окупаемости как цель — получаем обратно
        // текущий BuildCost (с точностью до округления decimal-деления в обе стороны).
        var roundTrip = row.MaxBuildCostForTargetPayback(row.PaybackTurns!.Value);
        Assert.Equal(row.BuildCost, roundTrip, 6);
    }

    /// <summary>Нулевая/отрицательная маржа (см. <see cref="PaybackTurns_Is_Null_When_The_Factory_Has_No_Recurring_Cost_Basis"/>) — допустимый BuildCost тоже 0, не бесконечность и не отрицательное число.</summary>
    [Fact]
    public void MaxBuildCostForTargetPayback_Is_Zero_When_The_Factory_Has_No_Recurring_Cost_Basis()
    {
        var config = BuildSingleFactoryConfig(buildCost: 1000m, fixedCostPerTurn: 0m, productionRate: 100m, salaryPerWorkerPerTurn: 0m);

        var rows = ProductionCostLevelCalculator.Calculate(config, workersPerFactory: 1);
        var row = rows.Single();

        Assert.Equal(0m, row.MaxBuildCostForTargetPayback(75m));
    }

    /// <summary>Один сектор, одна фабрика уровня 0, без входов — минимум, достаточный для проверки самой формулы.</summary>
    /// <summary>internal, не private — переиспользуется <c>TeamSteadyStateCalculatorTests</c> (та же сборка).</summary>
    internal static ResolvedGameConfig BuildSingleFactoryConfig(
        decimal buildCost, decimal fixedCostPerTurn, decimal productionRate, decimal salaryPerWorkerPerTurn = 5m)
    {
        var config = new GameConfig
        {
            Sectors = [new SectorConfig { Id = "A", Name = "Металлургия" }],
            Materials = [new MaterialConfig { Id = "ore", Name = "Руда", SectorId = "A", Level = 0 }],
            Recipes =
            [
                new RecipeConfig { Id = "ore-mining", OutputMaterialId = "ore", OutputQuantity = 1m, Inputs = [], ProductionRate = productionRate },
            ],
            FactoryDefinitions =
            [
                new FactoryDefinitionConfig { Id = "mine", Name = "Рудник", SectorId = "A", RecipeIds = ["ore-mining"], BuildCost = buildCost, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = fixedCostPerTurn },
            ],
            StartingConditions = new StartingConditionsConfig { MaxInitialBuildBudget = 100_000m },
            SessionPresets = [new SessionPresetConfig { Id = "short", Name = "Короткая", MinTurns = 5, MaxTurns = 5, TurnDurationMinutes = 1 }],
            PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                BaseMarketPerMaterial = [new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 1_000_000m }],
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
                SalaryPerWorkerPerTurn = salaryPerWorkerPerTurn,
            },
            Rnd = new RndConfig { ResearchPointThresholdsByLevel = [100m], DiminishingReturnsExponent = 1m, ProductionRateBonusPerLevel = 0.1m, MaxCommitmentPerTurn = 200m },
            Wear = new WearConfig
            {
                GracePeriodTurns = 1000, BaseWearRatePerTurn = 0.01m, AccelerationFactorPerTurn = 0.004m, MaxUpkeepPenaltyMultiplier = 0.5m,
                OverhaulTiers = [new OverhaulTierConfig { Id = "prevention", Name = "Профилактика", MinCondition = 0.9m, CostFraction = 0.02m, DurationTurns = 1, OutputMultiplier = 0.97m, SalaryRate = 1m, UpkeepRate = 1m }],
                CriticalConditionThreshold = 0.2m, ForcedRepairDurationTurns = 8, ForcedRepairSalaryRate = 0.66m, ForcedRepairUpkeepRate = 0.5m, PostForcedRepairCondition = 0.85m,
            },
            GenerationResearch = new GenerationResearchConfig { StartingGeneration = 1, ResearchPointThresholdsByGeneration = [], DiminishingReturnsExponent = 0.5m, MaxCommitmentPerTurn = 300m },
            Warehouse = new WarehouseConfig { FreeCapacity = 1_000_000m, OverageFeePerUnit = 0.1m },
            Reputation = new ReputationConfig { HalfLifeTurns = 10, WarmupTurns = 3, TerminationSeverityMultiplier = 3m },
            Contracts = new ContractsConfig { DeliveryMissPenaltyRate = 0.1m, TerminationPenaltyRate = 0.5m, VoluntaryTerminationFee = 100m, MaxActiveContractsPerTeam = null },
            Taxes = new TaxesConfig { PropertyTaxRatePerTurn = 0m, SalesTaxRate = 0m },
            News = [],
            FeatureFlags = new FeatureFlagsConfig { TaxesEnabled = false, EmergencyPurchaseEnabled = true },
        };

        return GameConfigLoader.Load(config);
    }

    /// <summary>Один сектор, три уровня друг на друге (2 единицы предыдущего материала на 1 единицу следующего) — минимум, достаточный проверить рост окупаемости по уровням.</summary>
    /// <summary>internal, не private — переиспользуется <c>TeamSteadyStateCalculatorTests</c> (та же сборка).</summary>
    internal static ResolvedGameConfig BuildThreeLevelChainConfig(decimal[] buildCosts, decimal[] fixedCosts, decimal[] productionRates)
    {
        var config = new GameConfig
        {
            Sectors = [new SectorConfig { Id = "A", Name = "Металлургия" }],
            Materials =
            [
                new MaterialConfig { Id = "material0", Name = "Материал 0", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "material1", Name = "Материал 1", SectorId = "A", Level = 1 },
                new MaterialConfig { Id = "material2", Name = "Материал 2", SectorId = "A", Level = 2 },
            ],
            Recipes =
            [
                new RecipeConfig { Id = "recipe0", OutputMaterialId = "material0", OutputQuantity = 1m, Inputs = [], ProductionRate = productionRates[0] },
                new RecipeConfig { Id = "recipe1", OutputMaterialId = "material1", OutputQuantity = 1m, Inputs = [new RecipeInputConfig { MaterialId = "material0", Quantity = 2m }], ProductionRate = productionRates[1] },
                new RecipeConfig { Id = "recipe2", OutputMaterialId = "material2", OutputQuantity = 1m, Inputs = [new RecipeInputConfig { MaterialId = "material1", Quantity = 2m }], ProductionRate = productionRates[2] },
            ],
            FactoryDefinitions =
            [
                new FactoryDefinitionConfig { Id = "factory0", Name = "Фабрика 0", SectorId = "A", RecipeIds = ["recipe0"], BuildCost = buildCosts[0], LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = fixedCosts[0] },
                new FactoryDefinitionConfig { Id = "factory1", Name = "Фабрика 1", SectorId = "A", RecipeIds = ["recipe1"], BuildCost = buildCosts[1], LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = fixedCosts[1] },
                new FactoryDefinitionConfig { Id = "factory2", Name = "Фабрика 2", SectorId = "A", RecipeIds = ["recipe2"], BuildCost = buildCosts[2], LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = fixedCosts[2] },
            ],
            StartingConditions = new StartingConditionsConfig { MaxInitialBuildBudget = 100_000m },
            SessionPresets = [new SessionPresetConfig { Id = "short", Name = "Короткая", MinTurns = 5, MaxTurns = 5, TurnDurationMinutes = 1 }],
            PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                BaseMarketPerMaterial =
                [
                    new MaterialMarketConfig { MaterialId = "material0", BasePrice = 10m, BaseCapacity = 1_000_000m },
                    new MaterialMarketConfig { MaterialId = "material1", BasePrice = 20m, BaseCapacity = 1_000_000m },
                    new MaterialMarketConfig { MaterialId = "material2", BasePrice = 40m, BaseCapacity = 1_000_000m },
                ],
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
            GenerationResearch = new GenerationResearchConfig { StartingGeneration = 1, ResearchPointThresholdsByGeneration = [], DiminishingReturnsExponent = 0.5m, MaxCommitmentPerTurn = 300m },
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
