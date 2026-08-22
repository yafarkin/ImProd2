using Game.Config;
using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Config.News;
using Game.Config.Session;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Обмен ботов через биржевой стакан (Блок 7.3.1) на конфиге с настоящей межсекторной
/// зависимостью — «Готово когда» из <c>docs/BUILD_PLAN.md</c> (Блок 7.3.1): партия проходит ботами
/// end-to-end, и материал реально течёт между секторами, а не только внутри своего (см. <see
/// cref="BotSessionRunnerTests"/> — на самодостаточном пилотном конфиге торговать ботам искренне
/// нечем). Собственный маленький конфиг (не полноразмерные файлы `production-models/*.json`) —
/// нарочно: те ещё не откалиброваны (<c>docs/production-staging.md</c>, «Открытые вопросы»),
/// зависимость там открывается только после многих ходов исследования поколений, а тест должен
/// быстро и детерминированно проверять сам механизм стакана, а не терпение генератора поколений.
/// </summary>
public class CrossSectorTradingTests
{
    [Fact]
    public void Two_Sector_Session_Completes_And_The_Cross_Sector_Input_Flows_Through_The_Order_Book()
    {
        var config = BuildTwoSectorConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");
        var aPart = config.Materials["a-part"];

        var teamAId = Ulid.NewUlid();
        var teamBId = Ulid.NewUlid();
        var teams = new[]
        {
            new TeamSpec { Id = teamAId, Name = "Команда А", SectorId = sectorA.Id },
            new TeamSpec { Id = teamBId, Name = "Команда Б", SectorId = sectorB.Id },
        };
        var bots = new[]
        {
            new SimpleBot(teamAId, sectorA, config),
            new SimpleBot(teamBId, sectorB, config),
        };

        var session = GameSession.StartWithEndTurn(config, "short", endTurn: 15, teams);
        BotSessionRunner.RunToCompletion(session, bots, new Random(1));

        Assert.True(session.State.IsFinished);
        Assert.True(session.VerifyIntegrity());

        // Б физически не может произвести свой флагман без a-part от А (см. BuildTwoSectorConfig) —
        // если хоть одна поставка a-part продавцу-А/покупателю-Б состоялась, стакан действительно
        // провёл материал через границу секторов, а не только внутри своего.
        var crossSectorDelivery = session.Entries.Any(entry =>
        {
            if (entry.Change is not ContractDelivered delivered)
            {
                return false;
            }

            var contract = session.State.Contracts[delivered.ContractId];
            return contract.SellerTeamId == teamAId && contract.BuyerTeamId == teamBId && contract.Terms.Material == aPart;
        });
        Assert.True(crossSectorDelivery, "Ни одна поставка a-part от А к Б не состоялась — материал не потёк между секторами.");

        // Ни у одной команды партия не должна закончиться в глубоком минусе — экономика элементарно сходится.
        foreach (var bot in bots)
        {
            Assert.True(session.State.Teams[bot.TeamId].Balance > -10_000m);
        }
    }

    /// <summary>
    /// Регрессия на баг, найденный трассировкой (2026-08-21, rebalance/2-sector-stepwise): когда
    /// сектор Б покупает у сектора А не передел (<c>a-part</c>, себестоимость которого считается
    /// рекурсивно по рецепту), а СЫРЬЁ напрямую — до фикса <see cref="SimpleBot.ComputeSellOrders"/>
    /// себестоимость сырья в <see cref="SimpleBot"/> берётся из живой рыночной котировки, а
    /// «пол+маржа» строился поверх ТОЙ ЖЕ котировки — продавец никогда не проходил фильтр <see
    /// cref="OrderBook.Match"/> (<c>price &gt;= LimitPrice</c>) ни на одном ходу. Без фикса эта
    /// партия не сходится вовсе — Б стоит всю партию без единой поставки руды.
    /// </summary>
    [Fact]
    public void Two_Sector_Session_Completes_When_The_Cross_Sector_Input_Is_Raw_Material_Itself()
    {
        var config = BuildTwoSectorConfigWithDirectRawMaterialCrossing();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");
        var ore = config.Materials["ore"];

        var teamAId = Ulid.NewUlid();
        var teamBId = Ulid.NewUlid();
        var teams = new[]
        {
            new TeamSpec { Id = teamAId, Name = "Команда А", SectorId = sectorA.Id },
            new TeamSpec { Id = teamBId, Name = "Команда Б", SectorId = sectorB.Id },
        };
        var bots = new[]
        {
            new SimpleBot(teamAId, sectorA, config),
            new SimpleBot(teamBId, sectorB, config),
        };

        var session = GameSession.StartWithEndTurn(config, "short", endTurn: 15, teams);
        BotSessionRunner.RunToCompletion(session, bots, new Random(1));

        Assert.True(session.State.IsFinished);
        Assert.True(session.VerifyIntegrity());

        var crossSectorRawDelivery = session.Entries.Any(entry =>
        {
            if (entry.Change is not ContractDelivered delivered)
            {
                return false;
            }

            var contract = session.State.Contracts[delivered.ContractId];
            return contract.SellerTeamId == teamAId && contract.BuyerTeamId == teamBId && contract.Terms.Material == ore;
        });
        Assert.True(crossSectorRawDelivery, "Ни одна поставка руды (сырья) от А к Б не состоялась — продавец сырья заблокирован собственным полом цены.");
    }

    /// <summary>Тот же граф, что <see cref="BuildTwoSectorConfig"/>, но Б покупает у А сырьё («ore») напрямую, а не передел («a-part») — сам А сектор больше не производит a-part вообще.</summary>
    private static ResolvedGameConfig BuildTwoSectorConfigWithDirectRawMaterialCrossing()
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
                new MaterialConfig { Id = "oil", Name = "Нефть", SectorId = "B", Level = 0 },
                new MaterialConfig { Id = "b-widget", Name = "Изделие Б", SectorId = "B", Level = 1 },
            },
            Recipes = new[]
            {
                new RecipeConfig { Id = "ore-mining", OutputMaterialId = "ore", OutputQuantity = 1m, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1m },
                new RecipeConfig { Id = "oil-drilling", OutputMaterialId = "oil", OutputQuantity = 1m, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1m },
                new RecipeConfig
                {
                    Id = "b-widget-from-oil-and-ore", OutputMaterialId = "b-widget", OutputQuantity = 1m,
                    Inputs = new[]
                    {
                        new RecipeInputConfig { MaterialId = "oil", Quantity = 2m },
                        new RecipeInputConfig { MaterialId = "ore", Quantity = 1m },
                    },
                    ProductionRate = 1m,
                },
            },
            FactoryDefinitions = new[]
            {
                new FactoryDefinitionConfig { Id = "mine-a", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "well-b", Name = "Скважина", SectorId = "B", RecipeIds = new[] { "oil-drilling" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "plant-b", Name = "Завод Б", SectorId = "B", RecipeIds = new[] { "b-widget-from-oil-and-ore" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
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
                    // Намеренно НЕ в "полосе сведения" (см. doc-comment BuildTwoSectorConfig) — регрессия
                    // как раз и проверяет, что для сырья это больше не нужно: продавец больше не требует
                    // маржи сверх собственной котировки (см. фикс ComputeSellOrders).
                    new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "oil", BasePrice = 10m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "b-widget", BasePrice = 40m, BaseCapacity = 100_000m },
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
            },
            Rnd = new RndConfig
            {
                ResearchPointThresholdsByLevel = new[] { 100m, 300m },
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
    /// Два сектора, один флагман зависит от другого напрямую (не по кругу — А самодостаточен, у Б
    /// прямая зависимость от А), оба генерации 1 (разблокировано с самого начала, без ожидания
    /// исследования поколений — тест должен сходиться за считаные ходы, не десятки):
    /// А: руда (0) → a-part (1, из руды, свой) — А ничего не должен покупать у Б, весь его излишек
    ///    a-part сверх собственного потребления (нулевого) уходит либо Б, либо системе.
    /// Б: нефть (0) → b-widget (1, из нефти (своё) + a-part (только А)) — без покупки a-part у Б
    ///    b-widget физически не производится, а значит и не продаётся — сходится только если стакан
    ///    свёл продавца-А с покупателем-Б.
    /// </summary>
    private static ResolvedGameConfig BuildTwoSectorConfig()
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
                new RecipeConfig { Id = "ore-mining", OutputMaterialId = "ore", OutputQuantity = 1m, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1m },
                new RecipeConfig
                {
                    Id = "a-part-from-ore", OutputMaterialId = "a-part", OutputQuantity = 1m,
                    Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 2m } }, ProductionRate = 1m,
                },
                new RecipeConfig { Id = "oil-drilling", OutputMaterialId = "oil", OutputQuantity = 1m, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1m },
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
                new FactoryDefinitionConfig { Id = "mine-a", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "plant-a", Name = "Завод А", SectorId = "A", RecipeIds = new[] { "a-part-from-ore" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "well-b", Name = "Скважина", SectorId = "B", RecipeIds = new[] { "oil-drilling" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig { Id = "plant-b", Name = "Завод Б", SectorId = "B", RecipeIds = new[] { "b-widget-from-oil-and-a-part" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
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
                    new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 100_000m },
                    // Между расчётной себестоимостью (2 руды по 10 = 20) плюс пол продавца (+5%=21) и
                    // потолок покупателя (+20%=24, см. MinSellMarginRate/MaxBuyPremiumRate в
                    // SimpleBot) — иначе котировка рынка сама по себе никогда не попадёт в полосу
                    // сведения заявок стакана (наступили на этот же грабель при первом прогоне теста).
                    new MaterialMarketConfig { MaterialId = "a-part", BasePrice = 23m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "oil", BasePrice = 10m, BaseCapacity = 100_000m },
                    new MaterialMarketConfig { MaterialId = "b-widget", BasePrice = 40m, BaseCapacity = 100_000m },
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
            },
            Rnd = new RndConfig
            {
                ResearchPointThresholdsByLevel = new[] { 100m, 300m },
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
            // Оба материала — уровня 0-1, StartingGeneration=1 разблокирует их с самого начала: тест
            // проверяет сам стакан, не терпение генератора поколений (см. doc-comment класса).
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
