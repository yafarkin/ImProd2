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
    /// как и в реальной сессии (AGENTS §2, правило 5). <paramref name="startingLoan"/> — заём для
    /// сценария теста (SPEC §5.1: в реальной игре команда берёт его сама, без предустановки) —
    /// применяется настоящим журналируемым событием <see cref="LoanTaken"/> через сам <paramref name="log"/>-эквивалент
    /// (не через <see cref="GameSession.TakeLoan"/> — тут вообще нет обёртки <see cref="GameSession"/>
    /// с её проверкой фазы), чтобы реплей-калькуляторы видели его как обычную сделку, а не только
    /// живое состояние команды.
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
            ConfigHash = Resolved.ContentHash,
            Teams = new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = SectorA.Id },
            },
        });

        if (startingLoan > 0)
        {
            log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = teamId, Amount = startingLoan });
        }

        return (log, state.Teams[teamId]);
    }

    /// <summary>Журнал сессии с двумя командами сектора А (для событий контрактов на уровне Apply); про <paramref name="startingLoan"/> — см. <see cref="StartSessionWithOneTeam"/>.</summary>
    public static (EventLog<GameSessionState> Log, Team Buyer, Team Seller) StartSessionWithTwoTeams(decimal startingLoan = 0m)
    {
        var state = new GameSessionState(Resolved);
        var log = new EventLog<GameSessionState>(state);
        var buyerId = Ulid.NewUlid();
        var sellerId = Ulid.NewUlid();

        log.Append(new SessionStarted
        {
            Id = Ulid.NewUlid(),
            PresetId = "test",
            EndTurn = 999,
            ConfigHash = Resolved.ContentHash,
            Teams = new[]
            {
                new TeamSpec { Id = buyerId, Name = "Покупатель", SectorId = SectorA.Id },
                new TeamSpec { Id = sellerId, Name = "Продавец", SectorId = SectorA.Id },
            },
        });

        if (startingLoan > 0)
        {
            log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = buyerId, Amount = startingLoan });
            log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = sellerId, Amount = startingLoan });
        }

        return (log, state.Teams[buyerId], state.Teams[sellerId]);
    }

    /// <summary>
    /// Полноценная сессия с одной командой сектора А (для сквозных сценариев через GameSession).
    /// <paramref name="startingLoan"/> — заём для сценария теста (SPEC §5.1: в реальной игре
    /// команда берёт его сама) — применяется как настоящее журналируемое событие
    /// <see cref="LoanTaken"/> через сам <see cref="EventLog{TState}"/> (не через
    /// <see cref="GameSession.TakeLoan"/> — та требует фазу решений, а сессия здесь возвращается
    /// ровно в фазе расчёта первого хода, как и раньше), чтобы реплей-калькуляторы
    /// (<see cref="TurnHistoryCalculator"/>, экспорт журнала) видели его как обычную сделку.
    /// </summary>
    public static (GameSession Session, Ulid TeamId) StartGameSessionWithOneTeam(decimal startingLoan = 100_000m)
    {
        var teamId = Ulid.NewUlid();
        var log = new EventLog<GameSessionState>(new GameSessionState(Resolved));
        var session = GameSession.StartWithEndTurn(
            log,
            "test",
            endTurn: 999,
            new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = SectorA.Id },
            });

        if (startingLoan > 0)
        {
            log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = teamId, Amount = startingLoan });
        }

        return (session, teamId);
    }

    /// <summary>Полноценная сессия с двумя командами сектора А (для сквозных сценариев через GameSession); про <paramref name="startingLoan"/> — см. <see cref="StartGameSessionWithOneTeam"/>.</summary>
    public static (GameSession Session, Ulid BuyerId, Ulid SellerId) StartGameSessionWithTwoTeams(decimal startingLoan = 100_000m)
    {
        var buyerId = Ulid.NewUlid();
        var sellerId = Ulid.NewUlid();
        var log = new EventLog<GameSessionState>(new GameSessionState(Resolved));
        var session = GameSession.StartWithEndTurn(
            log,
            "test",
            endTurn: 999,
            new[]
            {
                new TeamSpec { Id = buyerId, Name = "Покупатель", SectorId = SectorA.Id },
                new TeamSpec { Id = sellerId, Name = "Продавец", SectorId = SectorA.Id },
            });

        if (startingLoan > 0)
        {
            log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = buyerId, Amount = startingLoan });
            log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = sellerId, Amount = startingLoan });
        }

        return (session, buyerId, sellerId);
    }

    /// <summary>Пара согласованных заявок (от покупателя и продавца) на spot-поставку листа.</summary>
    public static (ContractProposal Buyer, ContractProposal Seller) MatchingSheetSpotProposals(
        Ulid buyerId, Ulid sellerId, decimal volume = 10m, decimal unitPrice = 20m,
        decimal penaltyRate = 0.1m, int effectiveTurn = 2, int deliveryTurn = 2)
    {
        var terms = new ContractTerms(
            ContractType.Spot, Sheet, volume, unitPrice, penaltyRate, effectiveTurn, deliveryTurn, recurringEndTurn: null);

        return (
            new ContractProposal(buyerId, sellerId, buyerId, terms),
            new ContractProposal(buyerId, sellerId, sellerId, terms));
    }

    /// <summary>
    /// Собирает вариант базового конфига с другой новостной библиотекой и/или сценарным трендом
    /// (Блок 6.3) — для тестов новостной ленты, которым не подходит пустая <c>News</c> у <see cref="Resolved"/>.
    /// </summary>
    public static ResolvedGameConfig BuildWithNews(
        IReadOnlyList<NewsItemConfig> news, IReadOnlyList<EconomyTrendPhaseConfig>? trendScenario = null) =>
        Build(news, trendScenario);

    /// <summary>
    /// Собирает вариант базового конфига с другими длительностями фаз хода (Блок 8.2) — для тестов
    /// таймера, которым нужны различающиеся между собой Settlement/Decision, а не одинаковые
    /// заглушки по умолчанию.
    /// </summary>
    public static ResolvedGameConfig BuildWithPhaseTiming(PhaseTimingConfig phaseTiming) =>
        Build(phaseTiming: phaseTiming);

    /// <summary>
    /// Собирает вариант базового конфига, где у сталелитейного завода два рецепта («лист» и
    /// «проволока», оба из руды) вместо одного (Блок 9.1) — для тестов переключения продукта
    /// фабрики, которым нужен реальный выбор, а не единственно возможный рецепт по умолчанию.
    /// </summary>
    public static ResolvedGameConfig BuildWithSecondMillRecipe() => Build(addSecondMillRecipe: true);

    /// <summary>
    /// Собирает вариант базового конфига с другими параметрами склада (Блок 9.2) — для сквозного
    /// теста платы за превышение через <see cref="GameSession.RunTick"/>, которому нужен лимит ниже
    /// заглушки по умолчанию (1000 единиц), недостижимой за один ход в тесте.
    /// </summary>
    public static ResolvedGameConfig BuildWithWarehouse(WarehouseConfig warehouse) => Build(warehouse: warehouse);

    /// <summary>
    /// Собирает вариант базового конфига с ненулевыми капитальными затратами фабрики
    /// (<see cref="FactoryDefinitionConfig.FixedCostPerTurn"/>, у обоих типов сразу) и/или
    /// переменной частью (<see cref="EconomyConfig.ElectricityConsumptionPerOutputUnit"/>) — для
    /// тестов <c>FactoryUpkeepPaid</c>/<c>TickFinanceStep</c>/<c>FactoryProduced.OverheadCost</c>,
    /// которым не подходит заглушка 0 по умолчанию у <see cref="Resolved"/>.
    /// </summary>
    public static ResolvedGameConfig BuildWithFactoryUpkeep(decimal fixedCostPerTurn = 0m, decimal electricityConsumptionPerOutputUnit = 0m) =>
        Build(fixedCostPerTurn: fixedCostPerTurn, electricityConsumptionPerOutputUnit: electricityConsumptionPerOutputUnit);

    /// <summary>
    /// Собирает вариант базового конфига с третьим переделом («катанка» из «листов», уровень 2) и
    /// низким <see cref="GenerationResearchConfig.StartingGeneration"/> (1) — для тестов ворот
    /// <c>GameSession.BuildFactory</c>/шага исследования поколений, которым нужна реальная
    /// разница между «уже разблокировано» и «ещё нет» (у <see cref="Resolved"/> пирамида не выше
    /// уровня 1 — там разблокировано всё с самого начала).
    /// </summary>
    public static ResolvedGameConfig BuildWithGenerationResearch(GenerationResearchConfig? generationResearch = null) =>
        Build(addThirdLevelFactory: true, generationResearch: generationResearch);

    /// <summary>
    /// Собирает вариант базового конфига с ненулевой надбавкой к множителю экстренной закупки за
    /// «давление» недавних закупок (Блок 9.2, запрос пользователя: наказывать зависимость от рынка) —
    /// для тестов эскалации цены, которым не подходит заглушка 0 по умолчанию у <see cref="Resolved"/>.
    /// </summary>
    public static ResolvedGameConfig BuildWithEmergencyPurchasePressure(decimal pressureMultiplierPerUnit) =>
        Build(emergencyPurchasePressureMultiplierPerUnit: pressureMultiplierPerUnit);

    private static ResolvedGameConfig Build(
        IReadOnlyList<NewsItemConfig>? news = null,
        IReadOnlyList<EconomyTrendPhaseConfig>? trendScenario = null,
        PhaseTimingConfig? phaseTiming = null,
        bool addSecondMillRecipe = false,
        WarehouseConfig? warehouse = null,
        decimal fixedCostPerTurn = 0m,
        decimal electricityConsumptionPerOutputUnit = 0m,
        bool addThirdLevelFactory = false,
        GenerationResearchConfig? generationResearch = null,
        decimal emergencyPurchasePressureMultiplierPerUnit = 0m)
    {
        // Третий передел («катанка» из «листов», уровень 2) — только для BuildWithGenerationResearch,
        // остальные тесты этого файла его не видят вообще (Concat с пустым массивом — no-op).
        var thirdLevelMaterials = addThirdLevelFactory
            ? new[] { new MaterialConfig { Id = "coil", Name = "Катанка", SectorId = "A", Level = 2 } }
            : Array.Empty<MaterialConfig>();
        var thirdLevelRecipes = addThirdLevelFactory
            ? new[]
            {
                new RecipeConfig
                {
                    Id = "coil-from-sheet", OutputMaterialId = "coil", OutputQuantity = 1m,
                    Inputs = new[] { new RecipeInputConfig { MaterialId = "sheet", Quantity = 1m } }, ProductionRate = 1m,
                },
            }
            : Array.Empty<RecipeConfig>();
        var thirdLevelFactoryDefinitions = addThirdLevelFactory
            ? new[]
            {
                new FactoryDefinitionConfig
                {
                    Id = "coil-plant", Name = "Прокатный стан", SectorId = "A", RecipeIds = new[] { "coil-from-sheet" },
                    BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = fixedCostPerTurn,
                },
            }
            : Array.Empty<FactoryDefinitionConfig>();

        var config = new GameConfig
        {
            Sectors = new[] { new SectorConfig { Id = "A", Name = "Металлургия" } },
            Materials = (addSecondMillRecipe
                ? new[]
                {
                    new MaterialConfig { Id = "ore", Name = "Железная руда", SectorId = "A", Level = 0 },
                    new MaterialConfig { Id = "sheet", Name = "Стальные листы", SectorId = "A", Level = 1 },
                    new MaterialConfig { Id = "wire", Name = "Проволока", SectorId = "A", Level = 1 },
                }
                : new[]
                {
                    new MaterialConfig { Id = "ore", Name = "Железная руда", SectorId = "A", Level = 0 },
                    new MaterialConfig { Id = "sheet", Name = "Стальные листы", SectorId = "A", Level = 1 },
                }).Concat(thirdLevelMaterials).ToArray(),
            Recipes = (addSecondMillRecipe
                ? new[]
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
                    new RecipeConfig
                    {
                        Id = "wire-from-ore", OutputMaterialId = "wire", OutputQuantity = 1m,
                        Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 1m } }, ProductionRate = 1m,
                    },
                }
                : new[]
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
                }).Concat(thirdLevelRecipes).ToArray(),
            FactoryDefinitions = new[]
            {
                // FixedCostPerTurn=0 по умолчанию — большинство тестов этого файла не про капитальные
                // затраты; тесты на них берут вариант через BuildWithFactoryUpkeep.
                new FactoryDefinitionConfig { Id = "iron-mine", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" }, BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = fixedCostPerTurn },
                new FactoryDefinitionConfig
                {
                    Id = "steel-mill", Name = "Сталелитейный завод", SectorId = "A",
                    RecipeIds = addSecondMillRecipe ? new[] { "sheet-from-ore", "wire-from-ore" } : new[] { "sheet-from-ore" },
                    BuildCost = 100m, LiquidationValueCoefficient = 0.5m, FixedCostPerTurn = fixedCostPerTurn,
                },
            }.Concat(thirdLevelFactoryDefinitions).ToArray(),
            StartingConditions = new StartingConditionsConfig
            {
                MaxStartingLoanAmount = 100_000m,
                BaseLoanInterestRate = 0.05m,
                LoanInterestRateGrowthPerUnitBorrowed = 0m,
                ForcedLoanPenaltyRatePerOccurrence = 0.1m,
                MaxReputationRatePenalty = 0.1m,
                MandatoryRepaymentRatePerTurn = 0m,
            },
            SessionPresets = new[]
            {
                new SessionPresetConfig { Id = "test", Name = "Test", MinTurns = 1, MaxTurns = 999, TurnDurationMinutes = 1 },
            },
            PhaseTiming = phaseTiming ?? new PhaseTimingConfig { SettlementPhaseSeconds = 1, DecisionPhaseSeconds = 1 },
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = 2m,
                // 0 по умолчанию — большинство тестов этого файла не про давление недавних закупок;
                // тесты на него используют BuildWithEmergencyPurchasePressure.
                EmergencyPurchasePressureMultiplierPerUnit = emergencyPurchasePressureMultiplierPerUnit,
                EmergencyPurchasePressureHalfLifeTurns = 3,
                BaseMarketPerMaterial = new[]
                {
                    new MaterialMarketConfig { MaterialId = "ore", BasePrice = 10m, BaseCapacity = 100m },
                    new MaterialMarketConfig { MaterialId = "sheet", BasePrice = 25m, BaseCapacity = 8m },
                },
                MarginMultiplierByProcessingLevel = new[]
                {
                    new ProcessingLevelMarginConfig { Level = 1, MarginMultiplier = 1.2m },
                },
                MarketCapacityOverflowDiscount = 0.5m,
                ElectricityBasePrice = 1m,
                // 0 по умолчанию — большинство тестов этого файла не про переменные затраты на
                // работу фабрики; тесты на них берут вариант через BuildWithFactoryUpkeep.
                ElectricityConsumptionPerOutputUnit = electricityConsumptionPerOutputUnit,
                TrendScenario = trendScenario ?? Array.Empty<EconomyTrendPhaseConfig>(),
                WarehouseLiquidationRate = 0.5m,
            },
            WorkerProductivity = new WorkerProductivityConfig
            {
                BaseWorkerCount = 5,
                DiminishingReturnsFactor = 0.5m,
                HireCostPerWorker = 50m,
                FireCostPerWorker = 30m,
                SalaryPerWorkerPerTurn = 5m,
                // Заметно выше, чем в любом сценарии этого файла набирается рабочих — большинство
                // тестов не про прогрессивную надбавку; тесты на неё используют собственный
                // WorkerProductivityConfig с низким порогом.
                TeamSalaryBaseWorkerCount = 1000,
                SalaryEscalationFactor = 1.5m,
            },
            Rnd = new RndConfig
            {
                ResearchPointThresholdsByLevel = new[] { 100m, 300m },
                // Экспонента 1 — очки исследований совпадают с накопленными ¤ один в один (линейно),
                // так что этот общий тестовый конфиг не ломает арифметику существующих тестов, которые
                // всюду считают пороги как сырые ¤, а не как очки. Нелинейная отдача (реальный p < 1,
                // как в живом конфиге) отдельно проверена в RndCalculatorTests.
                DiminishingReturnsExponent = 1m,
                ProductionRateBonusPerLevel = 0.1m,
                MaxCommitmentPerTurn = 200m,
            },
            // Материалы этого общего тестового конфига не заходят выше уровня 1 (ore=0, sheet=1) —
            // StartingGeneration=1 покрывает их целиком, ни один существующий тест, строящий фабрики
            // через GameSession.BuildFactory, не сломается. BuildWithGenerationResearch подставляет
            // свой конфиг с более глубокой пирамидой (третий передел, level 2) и настоящими порогами.
            GenerationResearch = generationResearch ?? new GenerationResearchConfig
            {
                StartingGeneration = 1,
                ResearchPointThresholdsByGeneration = Array.Empty<decimal>(),
                DiminishingReturnsExponent = 0.5m,
                MaxCommitmentPerTurn = 300m,
            },
            Warehouse = warehouse ?? new WarehouseConfig { FreeCapacity = 1000m, OverageFeePerUnit = 0.1m },
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
            News = news ?? Array.Empty<NewsItemConfig>(),
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
