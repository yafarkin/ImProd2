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
/// Запрос пользователя (docs/TODO.md #20, 2026-08-17): «научим бот строить все варианты фабрик с
/// каждым рецептом — смысл тот же, как все фабрики построить». До этой правки
/// <see cref="SimpleBot.BuildNewlyUnlockedFactories"/> строил ОДНУ фабрику на тип, и та молча получала
/// рецепт по умолчанию (<c>Recipes[0]</c>, см. <c>Factory.cs</c>) — ни один формульный бот не вызывал
/// <c>GameSession.SelectRecipe</c> (см. <c>docs/production-staging.md</c>, «Стадия 4»). Ни в одном
/// реальном production-model конфиге сейчас нет фабрики с &gt;1 рецептом (кроме
/// <c>metallurgy.json</c>/<c>flex-reprocessing-shop</c>, добавленной этой же правкой), поэтому старое
/// поведение проверяется отдельным маленьким самодостаточным конфигом здесь, не общим фикстур-файлом
/// (<c>PilotBotSession</c>) — чтобы не тащить риск для десятков других тестов, которые на нём держатся.
/// </summary>
public class MultiRecipeFactoryTests
{
    [Fact]
    public void BuildNewlyUnlockedFactories_BuildsOneFactoryPerRecipe_NotJustTheFirstOne()
    {
        var config = BuildTwoRecipeFactoryConfig();
        var sector = config.Sectors.Single();
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 5, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sector.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        var bot = new SimpleBot(teamId, sector, config, leverage: 1m);
        bot.BuildOutSectorChain(session);

        var flexFactories = session.State.Teams[teamId].Factories
            .Where(f => f.Definition.Id == "flex-mill")
            .ToList();

        Assert.Equal(2, flexFactories.Count);
        Assert.Contains(flexFactories, f => f.SelectedRecipe.Id == "alloy-x-from-ore");
        Assert.Contains(flexFactories, f => f.SelectedRecipe.Id == "alloy-y-from-ore");
    }

    [Fact]
    public void BuildNewlyUnlockedFactories_IsIdempotent_DoesNotRebuildAlreadyBuiltRecipeCombinations()
    {
        var config = BuildTwoRecipeFactoryConfig();
        var sector = config.Sectors.Single();
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 5, new[] { new TeamSpec { Id = teamId, Name = "Бот", SectorId = sector.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement(1) -> Decision(1)

        var bot = new SimpleBot(teamId, sector, config, leverage: 1m);
        bot.BuildOutSectorChain(session);
        var countAfterFirstCall = session.State.Teams[teamId].Factories.Count;

        // Тот же вызов, что реально идёт каждый ход решений (BuildNewlyUnlockedFactories) — не должен
        // штамповать новые фабрики повторно для комбинаций (тип, рецепт), которые уже построены.
        bot.BuildNewlyUnlockedFactories(session);

        Assert.Equal(countAfterFirstCall, session.State.Teams[teamId].Factories.Count);
    }

    /// <summary>Один сектор, одна сырьевая руда, одна фабрика-развилка с двумя независимыми однорецептными выходами — минимум, достаточный для проверки «строит обе комбинации, не только первую».</summary>
    private static ResolvedGameConfig BuildTwoRecipeFactoryConfig()
    {
        var config = new GameConfig
        {
            Sectors = new[]
            {
                new SectorConfig { Id = "A", Name = "Металлургия" },
            },
            Materials = new[]
            {
                new MaterialConfig { Id = "ore", Name = "Руда", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "alloy-x", Name = "Сплав X", SectorId = "A", Level = 1 },
                new MaterialConfig { Id = "alloy-y", Name = "Сплав Y", SectorId = "A", Level = 1 },
            },
            Recipes = new[]
            {
                new RecipeConfig { Id = "ore-mining", OutputMaterialId = "ore", OutputQuantity = 1m, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1000m },
                new RecipeConfig
                {
                    Id = "alloy-x-from-ore", OutputMaterialId = "alloy-x", OutputQuantity = 1m,
                    Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 2m } }, ProductionRate = 100m,
                },
                new RecipeConfig
                {
                    Id = "alloy-y-from-ore", OutputMaterialId = "alloy-y", OutputQuantity = 1m,
                    Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 2m } }, ProductionRate = 100m,
                },
            },
            FactoryDefinitions = new[]
            {
                new FactoryDefinitionConfig { Id = "mine", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m },
                new FactoryDefinitionConfig
                {
                    Id = "flex-mill", Name = "Гибкий завод", SectorId = "A",
                    RecipeIds = new[] { "alloy-x-from-ore", "alloy-y-from-ore" },
                    BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m,
                },
            },
            StartingConditions = new StartingConditionsConfig
            {
                MaxInitialBuildBudget = 100_000m,
            },
            SessionPresets = new[]
            {
                new SessionPresetConfig { Id = "short", Name = "Короткая", MinTurns = 5, MaxTurns = 5, TurnDurationMinutes = 1 },
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
                    new MaterialMarketConfig { MaterialId = "alloy-x", BasePrice = 50m, BaseCapacity = 1_000_000m },
                    new MaterialMarketConfig { MaterialId = "alloy-y", BasePrice = 50m, BaseCapacity = 1_000_000m },
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
}
