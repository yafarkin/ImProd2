using System.Text.Json;
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
/// Общий минимальный, но полный <see cref="ResolvedGameConfig"/> для тестов движка: один сектор,
/// цепочка руда → лист (рудник + сталелитейный завод), все обязательные секции GameConfig.
/// Единый источник вместо того, чтобы каждый тестовый файл заново собирал Sector/Material/Recipe
/// вручную — эти объекты графа конфигурации всё равно сравниваются по ссылке.
/// </summary>
internal static class TestGameConfig
{
    public static readonly ResolvedGameConfig Resolved = Build();

    public static Sector SectorA => Resolved.Sectors[0];
    public static Material Ore => Resolved.Materials["ore"];
    public static Material Sheet => Resolved.Materials["sheet"];
    public static FactoryDefinition Mine => Resolved.FactoryDefinitions.Single(f => f.Id == "iron-mine");
    public static FactoryDefinition Mill => Resolved.FactoryDefinitions.Single(f => f.Id == "steel-mill");

    /// <summary>
    /// Начинает сессию с одной зарегистрированной командой сектора А — то, что раньше в тестах
    /// делалось через `new Team(...)` напрямую, теперь обязано пройти через <see cref="SessionStarted"/>,
    /// как и в реальной сессии (AGENTS §2, правило 5).
    /// </summary>
    public static (EventLog<GameSessionState> Log, Team Team) StartSessionWithOneTeam(decimal startingLoan = 0m)
    {
        var state = new GameSessionState(Resolved);
        var log = new EventLog<GameSessionState>(state);
        var teamId = Ulid.NewUlid();

        log.Append(new SessionStarted
        {
            Id = Ulid.NewUlid(),
            PresetId = "test",
            EndTurn = 999,
            Teams = new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = SectorA.Id, StartingLoanAmount = startingLoan },
            },
        });

        return (log, state.Teams[teamId]);
    }

    private static ResolvedGameConfig Build()
    {
        var config = new GameConfig
        {
            Sectors = new[] { new SectorConfig { Id = "A", Name = "Металлургия" } },
            Materials = new[]
            {
                new MaterialConfig { Id = "ore", Name = "Железная руда", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "sheet", Name = "Стальные листы", SectorId = "A", Level = 1 },
            },
            Recipes = new[]
            {
                new RecipeConfig
                {
                    Id = "ore-mining", OutputMaterialId = "ore", OutputQuantity = 1m,
                    Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1m,
                },
                new RecipeConfig
                {
                    Id = "sheet-from-ore", OutputMaterialId = "sheet", OutputQuantity = 1m,
                    Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 2m } }, ProductionRate = 1m,
                },
            },
            FactoryDefinitions = new[]
            {
                new FactoryDefinitionConfig { Id = "iron-mine", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" } },
                new FactoryDefinitionConfig { Id = "steel-mill", Name = "Сталелитейный завод", SectorId = "A", RecipeIds = new[] { "sheet-from-ore" } },
            },
            StartingConditions = new StartingConditionsConfig
            {
                MaxStartingLoanAmount = 100_000m,
                BaseLoanInterestRate = 0.05m,
                LoanInterestRateGrowthPerUnitBorrowed = 0m,
                ForcedLoanPenaltyRatePerOccurrence = 0.1m,
            },
            SessionPresets = new[]
            {
                new SessionPresetConfig { Id = "test", Name = "Test", MinTurns = 1, MaxTurns = 999, TurnDurationMinutes = 1 },
            },
            PhaseTiming = new PhaseTimingConfig { CalculationPhaseSeconds = 1, DecisionPhaseSeconds = 1, CompletionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchasePriceMultiplier = 1m,
                MarginMultiplierByProcessingLevel = Array.Empty<ProcessingLevelMarginConfig>(),
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                TrendScenario = Array.Empty<EconomyTrendPhaseConfig>(),
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
                CumulativeInvestmentThresholdsByLevel = new[] { 100m, 300m },
                ProductionRateBonusPerLevel = 0.1m,
            },
            Warehouse = new WarehouseConfig { FreeCapacity = 1000m, OverageFeePerUnit = 0.1m },
            Reputation = new ReputationConfig { HalfLifeTurns = 10, WarmupTurns = 3 },
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

        return GameConfigLoader.Load(JsonSerializer.Serialize(config));
    }
}
