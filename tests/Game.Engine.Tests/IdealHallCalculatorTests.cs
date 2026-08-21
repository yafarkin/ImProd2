using Game.Config;
using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.News;
using Game.Config.Session;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Идеальный зал (Блок 7.3.4, <c>docs/production-balance.md</c> §4) — детерминированный расчёт X(t)
/// без бота и без движка.
/// </summary>
public class IdealHallCalculatorTests
{
    [Fact]
    public void Calculate_Throws_For_A_Null_Config()
    {
        Assert.Throws<ArgumentNullException>(() => IdealHallCalculator.Calculate(null!, 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Calculate_Throws_For_A_Non_Positive_Turn_Count(int maxTurns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IdealHallCalculator.Calculate(TestGameConfig.Resolved, maxTurns));
    }

    [Fact]
    public void Calculate_Returns_One_Trajectory_Per_Sector_With_MaxTurns_Entries()
    {
        var result = IdealHallCalculator.Calculate(TestGameConfig.Resolved, 10);

        var branch = Assert.Single(result.Branches);
        Assert.Equal("A", branch.SectorId);
        Assert.Equal(10, branch.ValueByTurn.Count);
    }

    [Fact]
    public void Calculate_Is_Deterministic()
    {
        var first = IdealHallCalculator.Calculate(TestGameConfig.Resolved, 15);
        var second = IdealHallCalculator.Calculate(TestGameConfig.Resolved, 15);

        Assert.Equal(first.Branches.Single().ValueByTurn, second.Branches.Single().ValueByTurn);
    }

    [Fact]
    public void Calculate_Grows_The_Value_Of_A_Self_Sufficient_Branch_Over_Time()
    {
        // Сектор А в BuildTwoSectorConfig ни от кого не зависит (свой рудник + завод) — чистая
        // проверка «идеальный зал вообще растит стоимость при прибыльной экономике», без завязки на
        // перевод между ветками (её проверяет Calculate_Lets_Value_Flow_...).
        // TestGameConfig.Resolved (общий конфиг тестов движка) тут не годится: его цены не рассчитаны
        // на 100%-ную инвестиционную интенсивность идеального зала и дают убыточную ветку — само по
        // себе честный результат (Блок 7.3.4 для того и существует, чтобы такое ловить), но не то,
        // что проверяет этот тест.
        var config = BuildTwoSectorConfig();

        var result = IdealHallCalculator.Calculate(config, 30);

        // Первые ходы — не показательны: эталонная политика вкладывает в R&D и командное
        // исследование поколений на потолок сразу у всех фабрик разом (doc-comment IdealHallCalculator)
        // — заметный проседающий рывок расходов раньше, чем накопится хоть какая-то выручка, X(t)
        // временно ныряет, потом карабкается вверх. Само падение-и-рост — не изъян, честная форма
        // «дорогого старта», сравниваем поздний участок между собой, не с самым первым ходом.
        var trajectory = result.Branches.Single(b => b.SectorId == "A").ValueByTurn;
        Assert.True(trajectory[^1] > trajectory[4], "X(T) должен быть заметно выше X(5) на достаточно длинной партии.");
    }

    [Fact]
    public void Calculate_Lets_Value_Flow_From_The_Producing_Branch_To_The_Dependent_One()
    {
        // Б физически не может произвести ничего сверх собственной нефти без a-part от А — если у
        // Б к концу партии положительная итоговая стоимость, значит совместная система (Блок 7.3.4,
        // «важная поправка» §4) действительно провела материал через границу секторов, а не только
        // внутри своего сектора.
        var config = BuildTwoSectorConfig();

        var result = IdealHallCalculator.Calculate(config, 30);

        var branchB = result.Branches.Single(b => b.SectorId == "B");
        Assert.True(branchB.ValueByTurn[^1] > 0m, "X(T) сектора Б должен быть положительным — материал от А должен был дойти.");
    }

    [Fact]
    public void Calculate_Sells_Uncontested_Surplus_To_The_System_Instead_Of_Leaving_It_Idle()
    {
        // Ветка добывает намного больше руды, чем сама же перерабатывает — остаток раньше просто
        // лежал на складе и оценивался по плоской BasePrice в конце хода (см. doc-comment класса,
        // «намеренно добавлено»); теперь он должен активно продаваться системе каждый ход по
        // себестоимости × MarketSaleCalculator.SystemSaleMarginMultiplier (фиксированная наценка,
        // с 2026-08-22 одна на все уровни передела — параметризовать нечем, раньше тест сравнивал
        // два уровня наценки между собой, см. историю до этой правки). Если бы излишек просто лежал
        // и не продавался активно каждый ход, X(5) вышел бы гораздо ниже (может, и в минус) — фабрики
        // платят зарплату и R&D каждый ход независимо от того, продаётся ли что-то.
        var config = BuildSingleSectorSurplusConfig();

        var value = IdealHallCalculator.Calculate(config, 5).Branches.Single().ValueByTurn[^1];

        Assert.True(value > 0m, $"X(5) = {value} должен быть положительным — излишек руды обязан продаваться системе активно каждый ход.");
    }

    /// <summary>
    /// Один сектор, руда добывается с большим запасом сверх того, что перерабатывает единственная
    /// фабрика — гарантированный необслуженный излишек руды (уровень 0) каждый ход, покупателя для
    /// него в конфиге нет вовсе (один сектор).
    /// </summary>
    private static ResolvedGameConfig BuildSingleSectorSurplusConfig()
    {
        var config = new GameConfig
        {
            Sectors = new[] { new SectorConfig { Id = "A", Name = "Металлургия" } },
            Materials = new[]
            {
                new MaterialConfig { Id = "ore", Name = "Руда", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "part", Name = "Деталь", SectorId = "A", Level = 1 },
            },
            Recipes = new[]
            {
                new RecipeConfig { Id = "ore-mining", OutputMaterialId = "ore", OutputQuantity = 1m, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1000m },
                new RecipeConfig
                {
                    Id = "part-from-ore", OutputMaterialId = "part", OutputQuantity = 1m,
                    Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 5m } }, ProductionRate = 1m,
                },
            },
            FactoryDefinitions = new[]
            {
                new FactoryDefinitionConfig { Id = "mine-a", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" }, BuildCost = 1m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "plant-a", Name = "Завод", SectorId = "A", RecipeIds = new[] { "part-from-ore" }, BuildCost = 1m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
            },
            StartingConditions = new StartingConditionsConfig
            {
                MaxInitialBuildBudget = 100_000m,
            },
            SessionPresets = new[]
            {
                new SessionPresetConfig { Id = "short", Name = "Короткая", MinTurns = 15, MaxTurns = 15, TurnDurationMinutes = 1 },
            },
            PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                BaseMarketPerMaterial = new[]
                {
                    new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 1_000_000m },
                    new MaterialMarketConfig { MaterialId = "part", BasePrice = 50m, BaseCapacity = 1_000_000m },
                },
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                ElectricityConsumptionPerOutputUnit = 0m,
                TrendScenario = Array.Empty<EconomyTrendPhaseConfig>(),
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
                // Пусто -> фабрики стартуют на максимальном уровне, обязательные 200/ход инвестиций в
                // R&D не списываются — с 2026-08-22 (фиксированная наценка продажи системе 1.05×) тонкая
                // маржа этого конфига (FixedCostPerTurn=0) их не покрывает, а тест не про R&D.
                ResearchPointThresholdsByLevel = Array.Empty<decimal>(),
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
                OverhaulTiers = new[]
                {
                    new OverhaulTierConfig { Id = "prevention", Name = "Профилактика", MinCondition = 0.9m, CostFraction = 0.02m, DurationTurns = 1, OutputMultiplier = 0.97m, SalaryRate = 1m, UpkeepRate = 1m },
                },
                CriticalConditionThreshold = 0.2m,
                ForcedRepairDurationTurns = 8,
                ForcedRepairSalaryRate = 0.66m,
                ForcedRepairUpkeepRate = 0.5m,
                PostForcedRepairCondition = 0.85m,
            },
            GenerationResearch = new GenerationResearchConfig
            {
                StartingGeneration = 1,
                ResearchPointThresholdsByGeneration = Array.Empty<decimal>(),
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
            News = Array.Empty<NewsItemConfig>(),
            FeatureFlags = new FeatureFlagsConfig
            {
                TaxesEnabled = false,
                EmergencyPurchaseEnabled = true,
            },
        };

        return GameConfigLoader.Load(config);
    }

    /// <summary>
    /// Та же цепочка (А самодостаточен, Б зависит от А напрямую), что <c>Game.Bots.Tests.CrossSectorTradingTests.BuildTwoSectorConfig</c>
    /// — см. её doc-comment за подробным разбором; здесь дополнительно нужна прибыльность обеих
    /// веток (не только сам факт сделки) — с 2026-08-21 цена продажи системе считается от
    /// себестоимости (<see cref="MaterialCostCalculator"/>), не от <c>BasePrice</c> — тот здесь
    /// влияет только на ёмкость рынка, значение самой цены больше не используется. С 2026-08-22
    /// наценка системной продажи фиксирована (<see cref="MarketSaleCalculator.SystemSaleMarginMultiplier"/>,
    /// 1.05×) и параметризовать её в этом фикстуре больше нечем — <c>FixedCostPerTurn=0</c> у всех
    /// фабрик специально, чтобы даже небольшой наценки хватало на зарплату и R&amp;D.
    /// </summary>
    internal static ResolvedGameConfig BuildTwoSectorConfig()
    {
        var config = new GameConfig
        {
            Sectors = new[]
            {
                new SectorConfig { Id = "A", Name = "Металлургия" },
                new SectorConfig { Id = "B", Name = "Нефтегазохимия" },
            },
            Materials = new[]
            {
                new MaterialConfig { Id = "ore", Name = "Руда", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "a-part", Name = "Деталь А", SectorId = "A", Level = 1 },
                new MaterialConfig { Id = "oil", Name = "Нефть", SectorId = "B", Level = 0 },
                new MaterialConfig { Id = "b-widget", Name = "Изделие Б", SectorId = "B", Level = 1 },
            },
            Recipes = new[]
            {
                // ProductionRate руды выше, чем нужно для собственного передела (plant-a хочет 10/ход
                // при полной мощности) — иначе весь выпуск без остатка уходит либо на свой же передел,
                // либо (a-part) по себестоимости в Б (см. doc-comment TransferAcrossBranches — обмен
                // между ветками без наценки), и веткa А никогда не продаёт что-либо системе напрямую
                // ни по какой марже, сколько её ни задирай (проверено экспериментом при переходе на
                // себестоимость, 2026-08-21) — тест как раз и должен проверять прибыль от продажи
                // системе, не только нулевой по деньгам трансфер.
                new RecipeConfig { Id = "ore-mining", OutputMaterialId = "ore", OutputQuantity = 1m, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 3m },
                new RecipeConfig
                {
                    Id = "a-part-from-ore", OutputMaterialId = "a-part", OutputQuantity = 1m,
                    Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 2m } }, ProductionRate = 1m,
                },
                // ProductionRate поднят с 1 до 3 (2026-08-22, симметрично ore-mining выше) — при 1
                // сектор Б своей нефти на прямую системную продажу почти не имел (весь тонкий выпуск
                // уходил в plant-b), а зарплата двух фабрик Б (well-b + plant-b) списывалась каждый ход
                // независимо; под фиксированной наценкой 1.05× такой Б устойчиво уходил в минус.
                new RecipeConfig { Id = "oil-drilling", OutputMaterialId = "oil", OutputQuantity = 1m, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 3m },
                new RecipeConfig
                {
                    Id = "b-widget-from-oil-and-a-part", OutputMaterialId = "b-widget", OutputQuantity = 1m,
                    Inputs = new[]
                    {
                        new RecipeInputConfig { MaterialId = "oil", Quantity = 2m },
                        new RecipeInputConfig { MaterialId = "a-part", Quantity = 1m },
                    },
                    ProductionRate = 1m,
                },
            },
            FactoryDefinitions = new[]
            {
                new FactoryDefinitionConfig { Id = "mine-a", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" }, BuildCost = 1m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "plant-a", Name = "Завод А", SectorId = "A", RecipeIds = new[] { "a-part-from-ore" }, BuildCost = 1m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "well-b", Name = "Скважина", SectorId = "B", RecipeIds = new[] { "oil-drilling" }, BuildCost = 1m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "plant-b", Name = "Завод Б", SectorId = "B", RecipeIds = new[] { "b-widget-from-oil-and-a-part" }, BuildCost = 1m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
            },
            StartingConditions = new StartingConditionsConfig
            {
                MaxInitialBuildBudget = 100_000m,
            },
            SessionPresets = new[]
            {
                new SessionPresetConfig { Id = "short", Name = "Короткая", MinTurns = 15, MaxTurns = 15, TurnDurationMinutes = 1 },
            },
            PhaseTiming = new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                BaseMarketPerMaterial = new[]
                {
                    // BasePrice здесь больше ни на что не влияет (см. doc-comment BuildTwoSectorConfig)
                    // — оставлены как заглушки, реальная прибыльность обеих веток задаётся себестоимостью
                    // и фиксированной наценкой продажи системе (MarketSaleCalculator.SystemSaleMarginMultiplier).
                    new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "a-part", BasePrice = 300m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "oil", BasePrice = 10m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "b-widget", BasePrice = 500m, BaseCapacity = 100_000m },
                },
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                ElectricityConsumptionPerOutputUnit = 0m,
                TrendScenario = Array.Empty<EconomyTrendPhaseConfig>(),
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
                // Пусто -> та же причина, что в BuildSingleSectorSurplusConfig выше.
                ResearchPointThresholdsByLevel = Array.Empty<decimal>(),
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
                OverhaulTiers = new[]
                {
                    new OverhaulTierConfig { Id = "prevention", Name = "Профилактика", MinCondition = 0.9m, CostFraction = 0.02m, DurationTurns = 1, OutputMultiplier = 0.97m, SalaryRate = 1m, UpkeepRate = 1m },
                },
                CriticalConditionThreshold = 0.2m,
                ForcedRepairDurationTurns = 8,
                ForcedRepairSalaryRate = 0.66m,
                ForcedRepairUpkeepRate = 0.5m,
                PostForcedRepairCondition = 0.85m,
            },
            GenerationResearch = new GenerationResearchConfig
            {
                StartingGeneration = 1,
                ResearchPointThresholdsByGeneration = Array.Empty<decimal>(),
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
            News = Array.Empty<NewsItemConfig>(),
            FeatureFlags = new FeatureFlagsConfig
            {
                TaxesEnabled = false,
                EmergencyPurchaseEnabled = true,
            },
        };

        return GameConfigLoader.Load(config);
    }
}
