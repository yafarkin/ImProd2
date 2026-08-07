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
                MandatoryRepaymentRatePerTurn = 0.05m,
                // Огромный — существующие тесты этого билдера не про потолок долга и не должны
                // неожиданно словить недостачу принудительного займа.
                MaxTotalDebt = 1_000_000_000m,
            },
            SessionPresets = new[]
            {
                new SessionPresetConfig { Id = "short", Name = "Short", MinTurns = 1, MaxTurns = 2, TurnDurationMinutes = 1 },
            },
            PhaseTiming = new PhaseTimingConfig
            {
                SettlementPhaseSeconds = 1,
                DecisionPhaseSeconds = 1,
            },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 1m,
                EmergencyPurchasePressureMultiplierPerUnit = 0m,
                EmergencyPurchasePressureHalfLifeTurns = 1,
                BaseMarketPerMaterial = Array.Empty<MaterialMarketConfig>(),
                MarginMultiplierByProcessingLevel = Array.Empty<ProcessingLevelMarginConfig>(),
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                ElectricityConsumptionPerOutputUnit = 0m,
                TrendScenario = Array.Empty<EconomyTrendPhaseConfig>(),
                WarehouseLiquidationRate = 0.5m,
            },
            WorkerProductivity = new WorkerProductivityConfig
            {
                BaseWorkerCount = 1,
                DiminishingReturnsFactor = 0.5m,
                HireCostPerWorker = 1m,
                FireCostPerWorker = 1m,
                SalaryPerWorkerPerTurn = 1m,
                TeamSalaryBaseWorkerCount = 1000,
                SalaryEscalationFactor = 1.5m,
            },
            Rnd = new RndConfig
            {
                ResearchPointThresholdsByLevel = new[] { 100m, 300m, 600m },
                // Экспонента 1 — очки исследований совпадают с накопленными ¤ один в один, чтобы не
                // ломать арифметику существующих тестов, построенных на этом билдере (см. тот же приём
                // в TestGameConfig.cs). Реальная нелинейная отдача — в живых конфигах и RndCalculatorTests.
                DiminishingReturnsExponent = 1m,
                ProductionRateBonusPerLevel = 0.1m,
                MaxCommitmentPerTurn = 1000m,
            },
            Wear = new WearConfig
            {
                // GracePeriodTurns огромный — существующие тесты, построенные на этом билдере, не
                // рассчитаны на десятки ходов и не должны неожиданно словить износ/простой; сама
                // механика отдельно проверена в WearCalculatorTests/WearStepTests с собственным конфигом.
                GracePeriodTurns = 1000,
                BaseWearRatePerTurn = 0.01m,
                AccelerationFactorPerTurn = 0.004m,
                MaxUpkeepPenaltyMultiplier = 0.5m,
                OverhaulTiers = new[]
                {
                    new OverhaulTierConfig { Id = "prevention", Name = "Профилактика", MinCondition = 0.9m, CostFraction = 0.02m, DurationTurns = 1, OutputMultiplier = 0.97m, SalaryRate = 1m, UpkeepRate = 1m },
                    new OverhaulTierConfig { Id = "scheduled", Name = "Плановое обслуживание", MinCondition = 0.75m, CostFraction = 0.06m, DurationTurns = 1, OutputMultiplier = 0.85m, SalaryRate = 1m, UpkeepRate = 1m },
                    new OverhaulTierConfig { Id = "major", Name = "Капремонт", MinCondition = 0.55m, CostFraction = 0.15m, DurationTurns = 2, OutputMultiplier = 0.75m, SalaryRate = 0.66m, UpkeepRate = 0.5m },
                    new OverhaulTierConfig { Id = "heavy", Name = "Серьёзный ремонт", MinCondition = 0.35m, CostFraction = 0.25m, DurationTurns = 3, OutputMultiplier = 0.6m, SalaryRate = 0.66m, UpkeepRate = 0.5m },
                    new OverhaulTierConfig { Id = "reconstruction", Name = "Полная реконструкция", MinCondition = 0.2m, CostFraction = 0.4m, DurationTurns = 5, OutputMultiplier = 0.4m, SalaryRate = 0.66m, UpkeepRate = 0.5m },
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
                MaxCommitmentPerTurn = 1000m,
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
