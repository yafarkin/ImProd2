using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.News;
using Game.Config.Session;

namespace Game.Config.Tests;

/// <summary>
/// Собирает минимальный валидный GameConfig для тестов валидатора/загрузчика, чтобы каждый тест
/// мог переопределить только интересующий его раздел каталога, не выписывая все обязательные
/// секции конфига заново.
/// </summary>
internal static class GameConfigTestBuilder
{
    public static GameConfig Build(
        IReadOnlyList<SectorConfig>? sectors = null,
        IReadOnlyList<MaterialConfig>? materials = null,
        IReadOnlyList<RecipeConfig>? recipes = null,
        IReadOnlyList<FactoryDefinitionConfig>? factoryDefinitions = null)
    {
        return new GameConfig
        {
            Sectors = sectors ?? new[] { new SectorConfig { Id = "A", Name = "Sector A" } },
            Materials = materials ?? Array.Empty<MaterialConfig>(),
            Recipes = recipes ?? Array.Empty<RecipeConfig>(),
            FactoryDefinitions = factoryDefinitions ?? Array.Empty<FactoryDefinitionConfig>(),
            StartingConditions = new StartingConditionsConfig
            {
                MaxStartingLoanAmount = 1000m,
                BaseLoanInterestRate = 0.05m,
                LoanInterestRateGrowthPerUnitBorrowed = 0m,
                ForcedLoanPenaltyRatePerOccurrence = 0.05m,
                MaxReputationRatePenalty = 0.1m,
            },
            SessionPresets = new[]
            {
                new SessionPresetConfig { Id = "short", Name = "Short", MinTurns = 1, MaxTurns = 2, TurnDurationMinutes = 1 },
            },
            PhaseTiming = new PhaseTimingConfig
            {
                CalculationPhaseSeconds = 1,
                DecisionPhaseSeconds = 1,
                CompletionPhaseSeconds = 1,
            },
            Economy = new EconomyConfig
            {
                EmergencyPurchasePriceMultiplier = 1m,
                BaseMarketPerMaterial = Array.Empty<MaterialMarketConfig>(),
                MarginMultiplierByProcessingLevel = Array.Empty<ProcessingLevelMarginConfig>(),
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                TrendScenario = Array.Empty<EconomyTrendPhaseConfig>(),
            },
            WorkerProductivity = new WorkerProductivityConfig
            {
                BaseWorkerCount = 1,
                DiminishingReturnsFactor = 0.5m,
                HireCostPerWorker = 1m,
                FireCostPerWorker = 1m,
                SalaryPerWorkerPerTurn = 1m,
            },
            Rnd = new RndConfig
            {
                CumulativeInvestmentThresholdsByLevel = new[] { 100m, 300m, 600m },
                ProductionRateBonusPerLevel = 0.1m,
            },
            Warehouse = new WarehouseConfig { FreeCapacity = 1m, OverageFeePerUnit = 0.1m },
            Reputation = new ReputationConfig { HalfLifeTurns = 1, WarmupTurns = 0, TerminationSeverityMultiplier = 1m },
            Contracts = new ContractsConfig
            {
                DeliveryMissPenaltyRate = 0.1m,
                TerminationPenaltyRate = 0.2m,
                VoluntaryTerminationFee = 1m,
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
    }
}
