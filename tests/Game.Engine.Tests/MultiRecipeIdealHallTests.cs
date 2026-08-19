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
/// Тот же запрос пользователя, что у <c>Game.Bots.Tests.MultiRecipeFactoryTests</c> (docs/TODO.md #20,
/// 2026-08-17) — «идеальный зал» должен строить одну фабрику НА КАЖДЫЙ рецепт многорецептного типа,
/// тем же приёмом, что и <see cref="SimpleBot"/> (иначе он перестаёт быть честной верхней границей для
/// реального бота, который теперь это умеет — недооценённый потолок сделал бы конвергенцию реального
/// бота выглядящей лучше, чем она есть). Публичный API <see cref="IdealHallCalculator.Calculate"/> не
/// раскрывает список построенных фабрик веток (внутреннее состояние приватно) — поэтому проверка
/// косвенная: сравнение накопленной ценности ветки с одним рецептом против веток с двумя, при прочих
/// равных. Если бы второй рецепт молча игнорировался (как было до правки), обе величины совпадали бы.
/// </summary>
public class MultiRecipeIdealHallTests
{
    [Fact]
    public void Calculate_BuildsAFactoryForEachRecipe_SecondRecipeMeasurablyRaisesBranchValue()
    {
        var oneRecipeConfig = BuildFlexFactoryConfig(includeSecondRecipe: false);
        var twoRecipeConfig = BuildFlexFactoryConfig(includeSecondRecipe: true);

        var oneRecipeResult = IdealHallCalculator.Calculate(oneRecipeConfig, maxTurns: 10);
        var twoRecipeResult = IdealHallCalculator.Calculate(twoRecipeConfig, maxTurns: 10);

        var oneRecipeFinalValue = oneRecipeResult.Branches.Single().ValueByTurn[^1];
        var twoRecipeFinalValue = twoRecipeResult.Branches.Single().ValueByTurn[^1];

        // Не точное удвоение (обе фабрики-развилки делят одну и ту же руду, что подрезает предельный
        // выигрыш от второй) — реально наблюдаемый прирост ~28% (825600 -> 1056900), порог ниже этого
        // с запасом: важна не точная цифра, а то, что прирост есть и заметен, а не 0% (как было бы,
        // если бы calculator по-прежнему молча брал только Recipes[0]).
        Assert.True(
            twoRecipeFinalValue > oneRecipeFinalValue * 1.15m,
            $"Второй рецепт должен был поднять итоговую ценность ветки заметно выше, чем с одним " +
            $"(один={oneRecipeFinalValue}, два={twoRecipeFinalValue}).");
    }

    private static ResolvedGameConfig BuildFlexFactoryConfig(bool includeSecondRecipe)
    {
        var flexRecipeIds = includeSecondRecipe
            ? new[] { "alloy-x-from-ore", "alloy-y-from-ore" }
            : new[] { "alloy-x-from-ore" };

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
                    RecipeIds = flexRecipeIds,
                    BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = 0m,
                },
            },
            StartingConditions = new StartingConditionsConfig
            {
                MaxStartingLoanAmount = 100_000m,
                BaseLoanInterestRate = 0.05m,
                LoanInterestRateGrowthPerUnitBorrowed = 0m,
                ForcedLoanPenaltyRatePerOccurrence = 0.1m,
                MaxReputationRatePenalty = 0.1m,
                MandatoryRepaymentRatePerTurn = 0m,
                MaxTotalDebt = 1_000_000_000m,
                MaxLoanInterestRate = 1_000_000_000m,
            },
            SessionPresets = new[]
            {
                new SessionPresetConfig { Id = "short", Name = "Короткая", MinTurns = 10, MaxTurns = 10, TurnDurationMinutes = 1 },
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
                MarginMultiplierByProcessingLevel = new[]
                {
                    new ProcessingLevelMarginConfig { Level = 1, MarginMultiplier = 1.2m },
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
            Deposits = new DepositsConfig { InterestRatePerTurn = 0m },
            News = Array.Empty<NewsItemConfig>(),
            FeatureFlags = new FeatureFlagsConfig
            {
                TaxesEnabled = false,
                DepositsEnabled = false,
                EmergencyPurchaseEnabled = true,
            },
        };

        return GameConfigLoader.Load(config);
    }
}
