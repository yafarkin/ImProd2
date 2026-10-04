using Game.Config.Economy;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Components;

namespace Game.Web;

/// <summary>
/// Модель экрана команды (<c>/team</c> и его разделы, <c>docs/manager-ui/README.md</c> §8 блок 1): все
/// данные, которые экран показывает, и все действия игрока. Раньше это был <c>@code</c> одного
/// <c>Team.razor</c> на две с лишним тысячи строк, и разделы экрана не могли жить отдельными
/// компонентами. Один экземпляр на открытую страницу: его держит оболочка, а компоненты разделов
/// получают его параметром. Разделы между собой экземпляр не делят — переход по разделам открывает
/// страницу заново, — поэтому то, что должно пережить переход (какую фабрику открыть), передаётся
/// через адрес.
/// </summary>
public sealed class TeamScreen
{
    public sealed record FactoryRow(
        Ulid FactoryId, string DefinitionName, int Level, int Workers, int DesiredWorkers,
        Material ProducedMaterial, string SelectedRecipeName, IReadOnlyList<(string Id, string Name)> RecipeOptions,
        IReadOnlyList<RecipeInput> Inputs,
        bool HasUnitCost, decimal UnitCost, IReadOnlyList<DashboardDisplay.PyramidRow> Pyramid,
        decimal RndInvestment, decimal RndPoints, decimal RndCommitmentPerTurn, decimal? NextLevelThreshold, decimal AllocationShare,
        decimal Condition, bool IsUnderRepair, int RepairTurnsRemaining, bool OverhaulRequested,
        decimal UpkeepPenaltyMultiplier, string OverhaulTierName, decimal OverhaulTierCost,
        int OverhaulTierDurationTurns, decimal OverhaulTierOutputMultiplier,
        IReadOnlyList<string> ConsumingFactoryNames,
        FactoryProfitabilityCalculator.FactoryProfitabilityEstimate? Profitability,
        ProductionCalculator.CapacityBreakdown CapacityBreakdown,
        IReadOnlyList<FactoryActivityRow> ActivityHistory,
        decimal LiquidationValue,
        bool InstantHiring);

    /// <summary>Одна строка таблицы «История по ходам» — что фабрика потребила со склада и сколько произвела за тот же ход (оба значения из одного и того же события <see cref="FactoryProduced"/>).</summary>
    public sealed record FactoryActivityRow(int Turn, string ConsumedSummary, decimal OutputQuantity);

    public sealed record BuildableDefinition(string Id, string Name, decimal BuildCost, decimal FixedCostPerTurn);

    public sealed record ContractRow(
        Ulid ContractId, string Role, string CounterpartyName, string MaterialName,
        ContractType Type, decimal Volume, decimal UnitPrice, decimal PenaltyRate,
        ContractStatus Status, string StatusLabel, string ConfirmationCode,
        int EffectiveTurn, int? SpotDeliveryTurn, int? RecurringEndTurn,
        bool HasPendingRevision, bool PendingRevisionIsMine,
        decimal? PendingVolume, decimal? PendingUnitPrice, decimal? PendingPenaltyRate, int? PendingRecurringEndTurn,
        bool WeAreTheProposer);

    /// <summary>
    /// Строка таблицы заявок на сделку (docs/TODO.md №16). У входящей заявки <see cref="Volume"/> и
    /// <see cref="UnitPrice"/> намеренно <c>null</c> — чужие условия на экран не попадают вовсе, а не
    /// прячутся разметкой: узнать их можно только у человека, в этом и смысл механики.
    /// </summary>
    public sealed record ProposalRow(
        Ulid ProposalId, bool Incoming, string CounterpartyName, string MaterialName,
        decimal? Volume, decimal? UnitPrice, int SubmittedOnTurn);

    public sealed record StockRow(string MaterialId, string MaterialName, Material Material, decimal Quantity);

    public sealed record MaterialOption(string Id, string Name);

    /// <summary>
    /// Строка «Состав команды» — общий вид для двух источников: до старта сессии это черновик
    /// (<see cref="GameSessionHost.StagedParticipants"/>), после — уже настоящие участники живой
    /// сессии (<see cref="ParticipantRegistration"/>). Управляющий может входить и добавлять
    /// переговорщиков ещё до старта (см. AddTeamMember), поэтому таблица обязана уметь показывать
    /// оба состояния одинаково.
    /// </summary>
    public sealed record TeamMemberRow(ParticipantRole Role, string DisplayName, string Code);

    private GameSessionHost Host { get; }

    /// <summary>Код входа вошедшего — нужен черновикам сделок переговорщика (SPEC §3).</summary>
    public string? ParticipantCode { get; }

    /// <summary>
    /// Срабатывает после любого действия игрока — оболочка страницы перерисовывает шапку и сообщение об
    /// ошибке сразу, не дожидаясь ежесекундного обновления.
    /// </summary>
    public event Action? Changed;

    public TeamScreen(GameSessionHost host, Ulid teamId, ParticipantRole role, string? participantCode, string? displayName)
    {
        ArgumentNullException.ThrowIfNull(host);

        Host = host;
        TeamId = teamId;
        Role = role;
        ParticipantCode = participantCode;
        DisplayName = displayName;

        lock (Host.SyncRoot)
        {
            if (Host.Session is not null && Host.Session.State.Teams.TryGetValue(teamId, out var team))
            {
                TeamName = team.Name;
            }
        }

        Refresh();
    }

    public string? DisplayName { get; set; }
    public ParticipantRole Role { get; set; }
    public string? TeamName { get; set; }
    public Ulid TeamId { get; set; }
    public bool SessionActive { get; set; }
    public IReadOnlyList<TeamMemberRow> TeamMembers { get; set; } = Array.Empty<TeamMemberRow>();
    public string NewMemberName { get; set; } = string.Empty;

    public int CurrentTurn { get; set; }
    public string PhaseLabel { get; set; } = string.Empty;
    public string PhaseExplanation { get; set; } = string.Empty;
    public bool IsDecisionPhase { get; set; }
    /// <summary>
    /// Может принимать решения (строить, нанимать, вкладывать в R&amp;D, продавать) —
    /// фаза «Решения» и роль «Управляющий»; переговорщик видит все те же панели, но в режиме чтения
    /// (запрос пользователя: «у переговорщика были те же права, что и у управляющего» — по факту
    /// действие раньше проверялось только по фазе, не по роли). Раздел «Сделки» — исключение,
    /// у неё уже есть своя, более тонкая проверка по ролям прямо в разметке (например, подтвердить
    /// сделку и принять пересмотр может только управляющий) — этот раздел не трогаем.
    /// </summary>
    public bool CanManage { get; set; }
    public string PhaseDuration { get; set; } = string.Empty;
    public string Countdown { get; set; } = string.Empty;
    public string? LastNewsHeadline { get; set; }
    public Ulid? EditingDraftId { get; set; }

    /// <summary>
    /// Элемент, к которому раздел должен прокрутить страницу после отрисовки, — например, карточка
    /// фабрики после перехода к ней из «Требует внимания». Раздел сбрасывает его, как только прокрутил.
    /// </summary>
    public string? PendingScrollTargetId { get; set; }

    /// <summary>Сколько решений ждут именно управляющего: черновики переговорщиков плюс контракты на его подтверждение.</summary>
    public int AwaitingManagerCount { get; set; }

    public decimal Balance { get; set; }
    public FinalScoreResult? Score { get; set; }
    public bool IsFinished { get; set; }
    public decimal ReputationPercentage { get; set; }
    public int ReputationSampleCount { get; set; }

    public IReadOnlyList<FactoryRow> Factories { get; set; } = Array.Empty<FactoryRow>();
    public IReadOnlyList<BuildableDefinition> BuildableDefinitions { get; set; } = Array.Empty<BuildableDefinition>();
    public IReadOnlyList<FactoryOverviewList.Node> FactoryOverviewRows { get; set; } = Array.Empty<FactoryOverviewList.Node>();
    public IReadOnlyList<TeamAttentionCalculator.AttentionItem> Attention { get; set; } = Array.Empty<TeamAttentionCalculator.AttentionItem>();
    public DashboardDisplay.AttentionNaming AttentionNaming { get; set; } = new(
        new Dictionary<Ulid, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());
    public int UnlockedGeneration { get; set; } = 1;
    public FactoryHistoryCalculator.TeamFactoryHistory? History { get; set; }
    public int HistoryEntryCount { get; set; } = -1;
    public bool ShowEconomyIndex { get; set; }
    public EconomyIndexDisplay.IndexSummary EconomyIndex { get; set; } = EconomyIndexDisplay.Describe([]);
    public LineChartDiagram.ChartLayout EconomyIndexChart { get; set; } = new([], [], [], 900, 240);
    public Ulid? SelectedFactoryId { get; set; }
    /// <summary>Отображаемое имя фабрики по Id — для истории операций (какая фабрика вызвала расход/доход), см. её построение в <see cref="Refresh"/>.</summary>
    public IReadOnlyDictionary<Ulid, string> FactoryDisplayNamesById { get; set; } = new Dictionary<Ulid, string>();
    public FactoryRow? SelectedFactory => Factories.FirstOrDefault(f => f.FactoryId == SelectedFactoryId);
    public string ActiveFactoryTab { get; set; } = "workers";
    public decimal HireCostPerWorker { get; set; }
    public decimal FireCostPerWorker { get; set; }
    public int MaxHiresPerTurn { get; set; } = int.MaxValue;
    public int BaseWorkerCount { get; set; }
    public Dictionary<string, decimal> TeamWarehouseByMaterialId { get; } = new();
    public Dictionary<string, decimal> BuildCosts { get; } = new();
    public Dictionary<string, decimal> UpkeepCosts { get; } = new();
    public Market? Market { get; set; }
    public EconomyConfig? EconomyConfig { get; set; }
    /// <summary>Себестоимость каждого материала (<see cref="MaterialCostCalculator"/>) — заменяет рыночную котировку как база цены продажи системе/аварийной закупки (запрос пользователя, rebalance/2-sector-stepwise, 2026-08-21).</summary>
    public IReadOnlyDictionary<string, decimal> MaterialCosts { get; set; } = new Dictionary<string, decimal>();
    public Dictionary<string, decimal> UnitCostByMaterialId { get; } = new();
    /// <summary>Реальная себестоимость остатка (взвешенное среднее фактических трат, <see cref="Warehouse.AverageCostOf"/>) — для превью продажи, в отличие от рыночной <see cref="UnitCostByMaterialId"/> (для карточки фабрики).</summary>
    public Dictionary<string, decimal> RealUnitCostByMaterialId { get; } = new();
    public IReadOnlyList<ContractRow> Contracts { get; set; } = Array.Empty<ContractRow>();
    public IReadOnlyList<ProposalRow> Proposals { get; set; } = Array.Empty<ProposalRow>();
    public IReadOnlyList<StockRow> Stock { get; set; } = Array.Empty<StockRow>();
    public IReadOnlyList<MaterialOption> EmergencyPurchaseMaterials { get; set; } = Array.Empty<MaterialOption>();

    public Dictionary<Ulid, string> SelectedRecipeIds { get; } = new();
    public Dictionary<Ulid, decimal> RndCommitmentAmounts { get; } = new();
    public decimal MaxRndCommitmentPerTurn { get; set; }
    public decimal GenerationResearchInvestment { get; set; }
    public decimal GenerationResearchPoints { get; set; }
    public decimal? NextGenerationThreshold { get; set; }
    public decimal GenerationResearchCommitmentPerTurn { get; set; }
    public decimal GenerationResearchCommitmentInput { get; set; }
    public decimal MaxGenerationResearchCommitmentPerTurn { get; set; }
    public Dictionary<Ulid, int> TargetWorkerCounts { get; } = new();
    public Dictionary<Ulid, decimal> AllocationShares { get; } = new();
    public Dictionary<string, decimal> SellVolumes { get; } = new();
    public IReadOnlyDictionary<string, decimal> PendingSaleVolumeByMaterial { get; set; } = new Dictionary<string, decimal>();
    public IReadOnlyDictionary<string, decimal> PendingEmergencyPurchaseVolumeByMaterial { get; set; } = new Dictionary<string, decimal>();
    public Dictionary<Ulid, decimal> RevisionVolumes { get; } = new();
    public Dictionary<Ulid, decimal> RevisionUnitPrices { get; } = new();
    public Dictionary<Ulid, decimal> RevisionPenaltyRates { get; } = new();
    public Dictionary<Ulid, int> RevisionEndTurns { get; } = new();
    public string? SelectedFactoryDefinitionId { get; set; }
    public string? ErrorMessage { get; set; }

    public decimal UsedCapacity { get; set; }
    public decimal FreeCapacity { get; set; }
    public decimal ProjectedWarehouseFee { get; set; }
    public bool EmergencyPurchaseEnabled { get; set; }
    public string? EmergencyPurchaseMaterialId { get; set; }
    public decimal EmergencyPurchaseVolume { get; set; } = 1m;

    public IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> FinanceHistory { get; set; } = Array.Empty<FinanceHistoryCalculator.FinanceOperation>();

    public void Refresh()
    {
        using (Host.EnterSyncRootTimed(nameof(TeamScreen) + "." + nameof(Refresh)))
        {
            if (Host.Session is null)
            {
                SessionActive = false;
                TeamMembers = Host.StagedParticipants
                    .Where(p => p.TeamId == TeamId)
                    .OrderBy(p => p.Role.ToString(), StringComparer.Ordinal)
                    .Select(p => new TeamMemberRow(p.Role, p.DisplayName, p.Code))
                    .ToList();
                return;
            }
            SessionActive = true;

            var state = Host.Session.State;

            CurrentTurn = state.CurrentTurn;
            IsFinished = state.IsFinished;
            // Полный реплей журнала — не бесплатная операция, а страница обновляется раз в секунду:
            // пересчитываем только когда в журнале появились новые записи, а не на каждый тик
            // таймера. Раньше ключом кеша был текущий ход — но решения (продать, построить фабрику)
            // добавляют события внутри одного и того же хода, поэтому кеш по ходу не замечал
            // их до следующего хода: свежее действие не появлялось в истории операций сразу же.
            var entryCount = Host.Session!.Entries.Count;
            if (History is null || HistoryEntryCount != entryCount)
            {
                History = FactoryHistoryCalculator.Summarize(Host.Session!.Entries, state.Config, TeamId);
                FinanceHistory = FinanceHistoryCalculator.Summarize(Host.Session!.Entries, state.Config, TeamId);
                var indexHistory = EconomyIndexHistoryCalculator.Summarize(Host.Session!.Entries, state.Config);
                EconomyIndex = EconomyIndexDisplay.Describe(indexHistory);
                EconomyIndexChart = EconomyIndexDisplay.BuildChart(indexHistory, 900, 240);
                HistoryEntryCount = entryCount;
            }
            ShowEconomyIndex = EconomyIndexDisplay.IsVisible(state.Config.Raw.Economy);
            PhaseLabel = PhaseDisplay.PhaseLabel(state.CurrentPhase);
            PhaseExplanation = PhaseDisplay.PhaseExplanation(state.CurrentPhase);
            IsDecisionPhase = state.CurrentPhase == TurnPhase.Decision;
            CanManage = IsDecisionPhase && Role == ParticipantRole.Manager;
            PhaseDuration = PhaseDisplay.FormatCountdown(PhaseTimerCalculator.TotalDuration(Host.Session!));
            Countdown = PhaseDisplay.FormatCountdown(PhaseTimerCalculator.Remaining(Host.Session!, DateTimeOffset.UtcNow));
            LastNewsHeadline = PhaseDisplay.FindLastNewsHeadline(Host.Session!);
            FreeCapacity = state.Config.Raw.Warehouse.FreeCapacity;
            EmergencyPurchaseEnabled = state.Config.Raw.FeatureFlags.EmergencyPurchaseEnabled;
            EmergencyPurchaseMaterials = state.Config.Materials.Values
                .Where(m => state.Market.HasQuote(m.Id))
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .Select(m => new MaterialOption(m.Id, m.Name))
                .ToList();
            if (EmergencyPurchaseMaterialId is null || EmergencyPurchaseMaterials.All(m => m.Id != EmergencyPurchaseMaterialId))
            {
                EmergencyPurchaseMaterialId = EmergencyPurchaseMaterials.FirstOrDefault()?.Id;
            }

            if (!state.Teams.TryGetValue(TeamId, out var team))
            {
                return;
            }

            TeamMembers = state.Participants.Values
                .Where(p => p.TeamId == TeamId)
                .OrderBy(p => p.Role.ToString(), StringComparer.Ordinal)
                .Select(p => new TeamMemberRow(p.Role, p.DisplayName, p.Code))
                .ToList();

            Balance = team.Balance;
            Score = FinalScoreCalculator.Calculate(team, MaterialCostCalculator.CalculateAll(state.Config), state.Config.Raw.FactoryDefinitions);
            PendingSaleVolumeByMaterial = team.PendingSaleVolumeByMaterial;
            PendingEmergencyPurchaseVolumeByMaterial = team.PendingEmergencyPurchaseVolumeByMaterial;

            var reputation = Host.Session!.GetReputation(TeamId);
            ReputationPercentage = reputation.Percentage;
            ReputationSampleCount = reputation.SampleCount;

            Market = state.Market;
            EconomyConfig = state.Config.Raw.Economy;
            MaterialCosts = MaterialCostCalculator.CalculateAll(state.Config);
            UnitCostByMaterialId.Clear();
            RealUnitCostByMaterialId.Clear();
            Stock = team.Warehouse.Stock.Select(s =>
            {
                SellVolumes.TryAdd(s.Material.Id, 1m);
                if (DashboardDisplay.TryCalculateUnitCost(s.Material, state, out var unitCost))
                {
                    UnitCostByMaterialId[s.Material.Id] = unitCost;
                }
                RealUnitCostByMaterialId[s.Material.Id] = team.Warehouse.AverageCostOf(s.Material);
                return new StockRow(s.Material.Id, s.Material.Name, s.Material, s.Quantity);
            }).ToList();
            UsedCapacity = Stock.Sum(s => s.Quantity);
            ProjectedWarehouseFee = WarehouseFeeCalculator.Calculate(UsedCapacity, state.Config.Raw.Warehouse).Fee;

            TeamWarehouseByMaterialId.Clear();
            foreach (var stockRow in team.Warehouse.Stock)
            {
                TeamWarehouseByMaterialId[stockRow.Material.Id] = stockRow.Quantity;
            }

            var profitabilityByFactoryId = new Dictionary<Ulid, FactoryProfitabilityCalculator.FactoryProfitabilityEstimate>();
            foreach (var factory in team.Factories)
            {
                var fixedCostPerTurn = state.Config.Raw.FactoryDefinitions.First(d => d.Id == factory.Definition.Id).FixedCostPerTurn;
                if (FactoryProfitabilityCalculator.TryCalculate(
                    factory, team.Factories, team.Warehouse, state.Market,
                    state.Config.Raw.WorkerProductivity, state.Config.Raw.Rnd, out var estimate,
                    fixedCostPerTurn, state.Config.Raw.Economy.ElectricityConsumptionPerOutputUnit,
                    MaterialCosts))
                {
                    profitabilityByFactoryId[factory.Id] = estimate;
                }
            }

            var rndConfig = state.Config.Raw.Rnd;
            var rndThresholds = rndConfig.ResearchPointThresholdsByLevel;
            MaxRndCommitmentPerTurn = rndConfig.MaxCommitmentPerTurn;

            var wearConfig = state.Config.Raw.Wear;

            var generationResearchConfig = state.Config.Raw.GenerationResearch;
            GenerationResearchInvestment = team.GenerationResearchInvestment;
            GenerationResearchPoints = GenerationResearchCalculator.CalculateResearchPoints(team.GenerationResearchInvestment, generationResearchConfig);
            NextGenerationThreshold = team.UnlockedGeneration - generationResearchConfig.StartingGeneration < generationResearchConfig.ResearchPointThresholdsByGeneration.Count
                ? generationResearchConfig.ResearchPointThresholdsByGeneration[team.UnlockedGeneration - generationResearchConfig.StartingGeneration]
                : (decimal?)null;
            GenerationResearchCommitmentPerTurn = team.GenerationResearchCommitmentPerTurn;
            MaxGenerationResearchCommitmentPerTurn = generationResearchConfig.MaxCommitmentPerTurn;
            if (GenerationResearchCommitmentInput == 0m)
            {
                GenerationResearchCommitmentInput = team.GenerationResearchCommitmentPerTurn;
            }

            HireCostPerWorker = state.Config.Raw.WorkerProductivity.HireCostPerWorker;
            FireCostPerWorker = state.Config.Raw.WorkerProductivity.FireCostPerWorker;
            MaxHiresPerTurn = state.Config.Raw.WorkerProductivity.MaxHiresPerTurn;
            BaseWorkerCount = state.Config.Raw.WorkerProductivity.BaseWorkerCount;

            // Фабрики не хранят собственное имя — только тип (Definition.Name) и Id. При нескольких
            // экземплярах одного типа это раньше давало неразличимые подписи (например, «Сейчас это
            // потребляют: Обогатительная фабрика, Обогатительная фабрика» — запрос пользователя).
            // Нумеруем так же, как уже нумерует карточки список фабрик (FactoryOverviewList) — по
            // порядку Id внутри типа, «№2» и далее со второго экземпляра. Сохраняем в поле, а не
            // локальную переменную — та же нумерация нужна и истории операций (см. «Фабрика» в
            // таблице ниже), не только карточкам фабрик.
            FactoryDisplayNamesById = team.Factories
                .GroupBy(f => f.Definition.Id)
                .SelectMany(group => group
                    .OrderBy(f => f.Id)
                    .Select((f, index) => (f.Id, Name: index > 0 ? $"{f.Definition.Name} №{index + 1}" : f.Definition.Name)))
                .ToDictionary(x => x.Id, x => x.Name);

            Factories = team.Factories.Select(factory =>
            {
                SelectedRecipeIds.TryAdd(factory.Id, factory.SelectedRecipe.Id);
                RndCommitmentAmounts.TryAdd(factory.Id, factory.RndCommitmentPerTurn);
                TargetWorkerCounts.TryAdd(factory.Id, factory.DesiredWorkers);
                AllocationShares.TryAdd(factory.Id, factory.AllocationShare);

                var hasUnitCost = DashboardDisplay.TryCalculateUnitCost(factory.SelectedRecipe.Output, state, out var unitCost);
                var pyramid = hasUnitCost
                    ? DashboardDisplay.FlattenPyramid(CostCalculator.BuildInputPyramid(factory.SelectedRecipe.Output, 1m, state.Config.RecipeBook))
                    : Array.Empty<DashboardDisplay.PyramidRow>();
                var nextLevelThreshold = factory.Level - 1 < rndThresholds.Count ? rndThresholds[factory.Level - 1] : (decimal?)null;
                var rndPoints = RndCalculator.CalculateResearchPoints(factory.RndInvestment, rndConfig);
                var upkeepPenaltyMultiplier = WearCalculator.CalculateUpkeepPenaltyMultiplier(factory.Condition, wearConfig);
                // Ступень, которая сработает, если команда закажет капремонт прямо сейчас — только
                // предпросмотр (та же формула, что применит WearStep в расчёте), null вне простоя не
                // бывает, если Condition < 1 (сама сессия следит, чтобы ступени покрывали весь диапазон
                // до CriticalConditionThreshold, см. doc-comment WearConfig.OverhaulTiers).
                var overhaulTier = factory.IsUnderRepair ? null : WearCalculator.SelectTier(factory.Condition, wearConfig.OverhaulTiers);
                var factoryDefinitionConfig = state.Config.Raw.FactoryDefinitions.First(d => d.Id == factory.Definition.Id);
                var overhaulTierCost = overhaulTier is null
                    ? 0m
                    : factoryDefinitionConfig.BuildCost * overhaulTier.CostFraction;
                // Та же формула, что и итоговый счёт в конце игры (см. FinalScoreCalculator) — команда
                // видит цену продажи заранее, до самого действия (запрос пользователя: «продажа
                // фабрики, как в реальном бизнесе»).
                var liquidationValue = factoryDefinitionConfig.BuildCost * factoryDefinitionConfig.LiquidationValueCoefficient;

                // Кто из своих же построенных фабрик заберёт этот выход как сырьё с общего склада —
                // прямой ответ на вопрос «куда идёт продукция» (см. пояснение в карточке ниже:
                // отдельного получателя задать нельзя, всё решает общий склад).
                var consumingFactoryNames = team.Factories
                    .Where(other => other.Id != factory.Id && other.SelectedRecipe.Inputs.Any(i => i.Material == factory.SelectedRecipe.Output))
                    .Select(other => FactoryDisplayNamesById[other.Id])
                    .ToList();

                var capacityBreakdown = ProductionCalculator.CalculateCapacityBreakdown(
                    factory, state.Config.Raw.WorkerProductivity, state.Config.Raw.Rnd);

                // Потребление и выпуск идут из одного и того же события FactoryProduced (см.
                // doc-comment FactoryHistoryCalculator.TeamFactoryHistory) — оба ряда всегда одной
                // длины и в одном порядке ходов, поэтому их можно просто состыковать по индексу.
                var outputSeries = History?.OutputByFactoryId.GetValueOrDefault(factory.Id) ?? Array.Empty<(int, decimal)>();
                var consumedSeries = History?.ConsumedInputsByFactoryId.GetValueOrDefault(factory.Id)
                    ?? Array.Empty<(int, IReadOnlyDictionary<string, decimal>)>();
                var activityHistory = outputSeries.Zip(consumedSeries, (output, consumed) => new FactoryActivityRow(
                    output.Turn,
                    consumed.ConsumedInputs.Count == 0
                        ? "—"
                        : string.Join(", ", consumed.ConsumedInputs.Select(pair => $"{state.Config.Materials[pair.Key].Name}: {pair.Value.ToString("0.##")}")),
                    output.OutputQuantity)).ToList();

                return new FactoryRow(
                    factory.Id, FactoryDisplayNamesById[factory.Id], factory.Level, factory.Workers, factory.DesiredWorkers,
                    factory.SelectedRecipe.Output, factory.SelectedRecipe.Output.Name,
                    factory.Definition.Recipes.Select(r => (r.Id, r.Output.Name)).ToList(),
                    factory.SelectedRecipe.Inputs,
                    hasUnitCost, unitCost, pyramid, factory.RndInvestment, rndPoints, factory.RndCommitmentPerTurn, nextLevelThreshold, factory.AllocationShare,
                    factory.Condition, factory.IsUnderRepair, factory.RepairTurnsRemaining, factory.OverhaulRequested,
                    upkeepPenaltyMultiplier, overhaulTier?.Name ?? string.Empty, overhaulTierCost,
                    overhaulTier?.DurationTurns ?? 0, overhaulTier?.OutputMultiplier ?? 0m,
                    consumingFactoryNames, profitabilityByFactoryId.GetValueOrDefault(factory.Id),
                    capacityBreakdown, activityHistory, liquidationValue, WorkforceStep.IsInstantHiring(factory));
            }).ToList();
            if (SelectedFactoryId is null || Factories.All(f => f.FactoryId != SelectedFactoryId))
            {
                SelectedFactoryId = Factories.FirstOrDefault()?.FactoryId;
            }
            // Раньше тип фабрики, который команда уже построила, пропадал из списка — ограничение
            // «один экземпляр на тип» было только здесь, в UI, а не в движке (GameSession.BuildFactory
            // его не проверяет). Команда осознанно может строить сколько угодно фабрик одного типа —
            // делить дефицитное сырьё между ними помогает AllocationShare (см. карточки фабрик ниже).
            var sectorDefinitions = state.Config.FactoryDefinitions.Where(d => d.Sector == team.Sector).ToList();
            UnlockedGeneration = team.UnlockedGeneration;
            BuildableDefinitions = sectorDefinitions
                .Where(d => d.Recipes[0].Output.Level <= UnlockedGeneration)
                .Select(d =>
                {
                    var raw = state.Config.Raw.FactoryDefinitions.First(f => f.Id == d.Id);
                    return new BuildableDefinition(d.Id, d.Name, raw.BuildCost, raw.FixedCostPerTurn);
                })
                .ToList();
            if (SelectedFactoryDefinitionId is null || BuildableDefinitions.All(d => d.Id != SelectedFactoryDefinitionId))
            {
                SelectedFactoryDefinitionId = BuildableDefinitions.FirstOrDefault()?.Id;
            }

            BuildCosts.Clear();
            UpkeepCosts.Clear();
            foreach (var definition in sectorDefinitions)
            {
                var raw = state.Config.Raw.FactoryDefinitions.First(f => f.Id == definition.Id);
                BuildCosts[definition.Id] = raw.BuildCost;
                UpkeepCosts[definition.Id] = raw.FixedCostPerTurn;
            }
            var lastTurnOutputByFactoryId = Factories
                .Where(f => f.ActivityHistory.Count > 0)
                .ToDictionary(f => f.FactoryId, f => f.ActivityHistory[^1].OutputQuantity);
            var theoreticalMaxOutputByFactoryId = Factories.ToDictionary(f => f.FactoryId, f => f.CapacityBreakdown.TheoreticalMaxOutput);
            FactoryOverviewRows = FactoryOverviewList.Build(
                sectorDefinitions, team.Factories, lastTurnOutputByFactoryId, theoreticalMaxOutputByFactoryId);

            // Панель «Требует внимания» (docs/TODO.md №7). Сам расчёт живёт в движке и оперирует
            // идентификаторами, здесь к ним добавляются только имена для подписей.
            Attention = TeamAttentionCalculator.Calculate(Host.Session!.Entries, state, TeamId);
            AttentionNaming = new DashboardDisplay.AttentionNaming(
                FactoryDisplayNamesById,
                state.Config.Materials.ToDictionary(pair => pair.Key, pair => pair.Value.Name),
                state.Config.Raw.Wear.OverhaulTiers.ToDictionary(tier => tier.Id, tier => tier.Name));

            Proposals = state.ContractProposals.Values
                .Where(p => p.Status == ContractProposalStatus.Open
                    && (p.SubmittedByTeamId == TeamId || p.CounterpartyTeamId == TeamId))
                .OrderBy(p => p.SubmittedOnTurn)
                .ThenBy(p => p.Id.ToString(), StringComparer.Ordinal)
                .Select(p =>
                {
                    var incoming = p.SubmittedByTeamId != TeamId;
                    var counterpartyId = incoming ? p.SubmittedByTeamId : p.CounterpartyTeamId;
                    var counterpartyName = state.Teams.TryGetValue(counterpartyId, out var counterparty) ? counterparty.Name : "?";

                    return new ProposalRow(
                        p.Id, incoming, counterpartyName, p.Proposal.Terms.Material.Name,
                        incoming ? null : p.Proposal.Terms.Volume,
                        incoming ? null : p.Proposal.Terms.UnitPrice,
                        p.SubmittedOnTurn);
                })
                .ToList();

            Contracts = state.Contracts.Values
                .Where(c => c.BuyerTeamId == TeamId || c.SellerTeamId == TeamId)
                .Select(c =>
                {
                    var counterpartyId = c.BuyerTeamId == TeamId ? c.SellerTeamId : c.BuyerTeamId;
                    var counterpartyName = state.Teams.TryGetValue(counterpartyId, out var counterparty) ? counterparty.Name : "?";
                    var pending = Host.Session!.GetPendingContractRevision(c.Id);
                    var weAreTheProposer = c.ProposedByTeamId == TeamId;
                    var statusLabel = c.Status == ContractStatus.PendingConfirmation
                        ? (weAreTheProposer ? "Ждёт подтверждения контрагента" : "Ждёт вашего подтверждения")
                        : DashboardDisplay.ContractStatusLabel(c.Status);

                    RevisionVolumes.TryAdd(c.Id, c.Terms.Volume);
                    RevisionUnitPrices.TryAdd(c.Id, c.Terms.UnitPrice);
                    RevisionPenaltyRates.TryAdd(c.Id, c.Terms.PenaltyRate);
                    RevisionEndTurns.TryAdd(c.Id, c.Terms.RecurringEndTurn ?? CurrentTurn);

                    return new ContractRow(
                        c.Id, c.BuyerTeamId == TeamId ? "Покупатель" : "Продавец", counterpartyName, c.Terms.Material.Name,
                        c.Terms.Type, c.Terms.Volume, c.Terms.UnitPrice, c.Terms.PenaltyRate,
                        c.Status, statusLabel, c.ConfirmationCode,
                        c.Terms.EffectiveTurn, c.Terms.SpotDeliveryTurn, c.Terms.RecurringEndTurn,
                        pending is not null, pending?.ProposingTeamId == TeamId,
                        pending?.Volume, pending?.UnitPrice, pending?.PenaltyRate, pending?.RecurringEndTurn,
                        weAreTheProposer);
                })
                .ToList();

            AwaitingManagerCount = Role != ParticipantRole.Manager
                ? 0
                : state.ContractDrafts.Values.Count(d => d.TeamId == TeamId && d.Status == ContractDraftStatus.AwaitingManager)
                  + Contracts.Count(c => c.Status == ContractStatus.PendingConfirmation && !c.WeAreTheProposer);
        }
    }

    public BuildableDefinition? SelectedBuildableDefinition =>
        BuildableDefinitions.FirstOrDefault(d => d.Id == SelectedFactoryDefinitionId);

    /// <summary>
    /// Не блокирует постройку (баланс может свободно уйти в минус, docs/TODO.md #23) — только
    /// управляет информационной подсказкой рядом с кнопкой «Построить».
    /// </summary>
    public bool CanAffordSelectedBuild => SelectedBuildableDefinition is not { } definition || Balance >= definition.BuildCost;

    public void BuildFactory()
    {
        if (SelectedFactoryDefinitionId is null)
        {
            return;
        }

        RunAction(() => Host.Session!.BuildFactory(TeamId, SelectedFactoryDefinitionId));
    }

    /// <summary>
    /// Фабрика, для которой на карточке сейчас показан вопрос «точно продать?» (Блок, запрос
    /// пользователя) — двухшаговое подтверждение прямо в разметке, без нативного <c>confirm()</c>
    /// (он блокирует поток выполнения браузера, что плохо сочетается с Blazor Server circuit).
    /// <see langword="null"/> — вопрос сейчас не показан ни для одной фабрики.
    /// </summary>
    public Ulid? FactoryPendingSaleId { get; set; }

    public void SellFactory(Ulid factoryId)
    {
        FactoryPendingSaleId = null;
        RunAction(() => Host.Session!.SellFactory(TeamId, factoryId));
    }

    /// <summary>Клик по узлу на схеме построенных фабрик — открывает карточку деталей этого экземпляра ниже.</summary>
    public void SelectFactory(Ulid factoryId) => SelectedFactoryId = factoryId;

    /// <summary>
    /// Фабрика, о которой говорит повод, — если он вообще про фабрику: у складского сбора и у
    /// поставки такой одной фабрики нет. У «материал кончается» их может быть несколько, берём
    /// первую — переход по ссылке это навигация, а не выбор за игрока, какой именно фабрикой
    /// заняться.
    /// </summary>
    public static Ulid? AttentionTargetFactoryId(TeamAttentionCalculator.AttentionItem item) => item switch
    {
        TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput x => x.FactoryId,
        TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers x => x.FactoryId,
        TeamAttentionCalculator.AttentionItem.FactoryInForcedDowntime x => x.FactoryId,
        TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive x => x.FactoryId,
        TeamAttentionCalculator.AttentionItem.MaterialRunningOut x => x.AffectedFactoryIds.Count > 0 ? x.AffectedFactoryIds[0] : null,
        _ => null,
    };

    /// <summary>
    /// Разница между полем ввода <see cref="TargetWorkerCounts"/> и уже объявленной численностью
    /// (<see cref="FactoryRow.DesiredWorkers"/>) — превью того, что изменится в объявлении при клике
    /// «Применить»: расчёт хода потом сам наймёт/уволит до этого значения и спишет стоимость один раз
    /// по итоговой разнице с фактическим <see cref="FactoryRow.Workers"/> (запрос пользователя: не
    /// списывать за каждое промежуточное значение, если команда меняла его несколько раз за ход — см.
    /// doc-comment <see cref="GameSession.SetWorkerCount"/>). Положительная разница — наём, отрицательная — увольнение.
    /// </summary>
    /// <summary>
    /// Сколько из объявленного прироста реально выйдет на смену уже на ближайшем расчёте — то же
    /// правило, что применит <see cref="WorkforceStep"/> (docs/TODO.md №25). Считаем здесь, а не
    /// зовём сам шаг: ему нужна доменная <c>Factory</c>, а у строки экрана её нет — правило же
    /// состоит из одного <c>Math.Min</c>, и оно сторожится тестом.
    /// </summary>
    public int HiresThisTurn(FactoryRow factory, int delta) =>
        factory.InstantHiring ? delta : Math.Min(delta, MaxHiresPerTurn);

    public (int Delta, decimal Cost) WorkerCountChange(FactoryRow factory)
    {
        var target = Math.Max(0, TargetWorkerCounts.GetValueOrDefault(factory.FactoryId, factory.DesiredWorkers));
        var delta = target - factory.Workers;
        var cost = delta switch
        {
            > 0 => delta * HireCostPerWorker,
            < 0 => -delta * FireCostPerWorker,
            _ => 0m,
        };
        return (delta, cost);
    }

    public bool CanApplyWorkerCount(FactoryRow factory)
    {
        var target = Math.Max(0, TargetWorkerCounts.GetValueOrDefault(factory.FactoryId, factory.DesiredWorkers));
        return target != factory.DesiredWorkers;
    }

    public void ApplyWorkerCount(FactoryRow factory)
    {
        var target = Math.Max(0, TargetWorkerCounts.GetValueOrDefault(factory.FactoryId, factory.DesiredWorkers));
        RunAction(() => Host.Session!.SetWorkerCount(TeamId, factory.FactoryId, target));
    }

    /// <summary>
    /// Снимает объявленную на этот ход численность, откатывая её обратно к уже фактической (<see
    /// cref="FactoryRow.Workers"/>) — сама отмена ничем не отличается от любого другого объявления
    /// (SPEC §4, §5.6), просто равна текущему значению.
    /// </summary>
    public void CancelWorkerCount(FactoryRow factory)
    {
        TargetWorkerCounts[factory.FactoryId] = factory.Workers;
        RunAction(() => Host.Session!.SetWorkerCount(TeamId, factory.FactoryId, factory.Workers));
    }

    public string TabClass(string tab) => ActiveFactoryTab == tab ? "active" : "";

    /// <summary>
    /// Остатки на складе команды по всей пирамиде сырья выбранной фабрики (руда → ... → продукт) —
    /// лог-шкала, потому что руды на складе обычно на порядки больше, чем готового продукта (запрос
    /// пользователя). Материалы пирамиды берутся уже посчитанными в <see cref="FactoryRow.Pyramid"/>
    /// (см. <see cref="Refresh"/>), цвет — по глубине пирамиды, той же палитрой, что и
    /// <see cref="SectorColors"/>.
    /// </summary>
    public LineChartDiagram.ChartLayout BuildStockChartLayout(FactoryRow factory)
    {
        var series = factory.Pyramid
            .GroupBy(row => row.Material.Id)
            .Select(group => (Material: group.First().Material, Depth: group.Min(row => row.Depth)))
            .OrderBy(entry => entry.Depth)
            .Select(entry => new LineChartDiagram.ChartSeries(
                entry.Material.Name,
                SectorColors.Palette[entry.Depth % SectorColors.Palette.Length],
                History!.StockByMaterialId.GetValueOrDefault(entry.Material.Id, Array.Empty<(int, decimal)>())))
            .Where(series => series.Points.Count > 0)
            .ToList();

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Logarithmic, 560, 220);
    }

    /// <summary>
    /// Остатки по ходам вообще всех материалов общего склада команды (не только пирамиды одной
    /// фабрики, как <see cref="BuildStockChartLayout"/>) — для склада в разделе «Производство». Тот же источник
    /// данных (<see cref="FactoryHistoryCalculator.TeamFactoryHistory.StockByMaterialId"/>) и та же
    /// лог-шкала: сырьё и готовый продукт отличаются на порядки.
    /// </summary>
    public LineChartDiagram.ChartLayout BuildWarehouseStockChartLayout()
    {
        var series = Stock
            .OrderBy(row => row.MaterialId, StringComparer.Ordinal)
            .Select((row, index) => new LineChartDiagram.ChartSeries(
                row.MaterialName,
                SectorColors.Palette[index % SectorColors.Palette.Length],
                History!.StockByMaterialId.GetValueOrDefault(row.MaterialId, Array.Empty<(int, decimal)>())))
            .Where(series => series.Points.Count > 0)
            .ToList();

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Logarithmic, 560, 240);
    }

    /// <summary>Объём фактического выпуска выбранной фабрики по ходам (событийные данные <see cref="FactoryProduced"/>, не оценка) — линейная шкала, один ряд.</summary>
    public LineChartDiagram.ChartLayout BuildOutputChartLayout(FactoryRow factory)
    {
        var points = History!.OutputByFactoryId.GetValueOrDefault(factory.FactoryId, Array.Empty<(int, decimal)>());
        var series = new[] { new LineChartDiagram.ChartSeries("Выпуск", SectorColors.Palette[0], points) };

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 560, 180);
    }

    /// <summary>Суммарная оценка прибыльности всей команды по уровням пирамиды сырья по ходам (не по одной фабрике — общекомандный блок страницы, см. разметку выше).</summary>
    public LineChartDiagram.ChartLayout BuildProfitByLevelChartLayout()
    {
        var series = History!.ProfitByLevel
            .OrderBy(pair => pair.Key)
            .Select(pair => new LineChartDiagram.ChartSeries(
                $"Уровень {pair.Key}", SectorColors.Palette[pair.Key % SectorColors.Palette.Length], pair.Value))
            .ToList();

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 560, 220, DashboardDisplay.FormatMoney);
    }

    /// <summary>
    /// Баланс команды по ходам (раздел «Аналитика») — может уходить ниже нуля, это не ошибка (SPEC
    /// §5.1/§5.9, пересмотрено — банковский заём убран как класс механики, docs/TODO.md #23).
    /// </summary>
    public LineChartDiagram.ChartLayout BuildBalanceChartLayout()
    {
        var series = new[] { new LineChartDiagram.ChartSeries("Баланс", SectorColors.Palette[0], History!.NetWorthByTurn) };

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 560, 220, DashboardDisplay.FormatMoney);
    }

    /// <summary>
    /// Отдельным графиком, а не вторым рядом на графике баланса: это две разные величины, и правило
    /// проекта — одна шкала на график (см. doc-comment <see cref="LineChartDiagram"/>). Разойтись
    /// они могут на порядок, если команда вложилась в фабрики.
    /// </summary>
    public LineChartDiagram.ChartLayout BuildScoreChartLayout()
    {
        var series = new[] { new LineChartDiagram.ChartSeries("Стоимость команды", SectorColors.Palette[1], History!.ScoreByTurn) };

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 560, 220, DashboardDisplay.FormatMoney);
    }

    /// <summary>
    /// Денежный поток по ходам (раздел «Аналитика», запрос пользователя: «важно видеть не только
    /// баланс под конец партии, но и зарабатывают ли сейчас деньги, и сколько») — сумма прихода
    /// минус сумма расхода за каждый ход по <see cref="FinanceHistory"/> (та же полная и уже
    /// проверенная разбивка операций, что показывает история операций в «Аналитике»), не накопленный итог, как
    /// <see cref="BuildBalanceChartLayout"/> выше. Сходится с разницей баланса между соседними
    /// точками <see cref="FactoryHistoryCalculator.TeamFactoryHistory.NetWorthByTurn"/> по построению
    /// — <see cref="FinanceHistoryCalculator"/> перечисляет каждую операцию, трогающую баланс
    /// команды, ни одна не пропущена и не задвоена. Ходы совсем без операций получают 0, а не
    /// выпадают из ряда, — берём полный диапазон ходов из <see cref="BuildBalanceChartLayout"/>, а не
    /// только те, где что-то произошло, чтобы ось ходов совпадала с графиком баланса выше.
    /// </summary>
    public LineChartDiagram.ChartLayout BuildCashFlowChartLayout()
    {
        var flowByTurn = FinanceHistory
            .GroupBy(operation => operation.Turn)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(operation =>
                    operation.Direction == FinanceHistoryCalculator.MoneyDirection.Income ? operation.Amount : -operation.Amount));

        var points = History!.NetWorthByTurn
            .Select(point => (point.Turn, flowByTurn.GetValueOrDefault(point.Turn)))
            .ToList();

        var series = new[] { new LineChartDiagram.ChartSeries("Поток", SectorColors.Palette[1], points) };

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 560, 220, DashboardDisplay.FormatMoney);
    }

    /// <summary>Репутация команды по ходам (раздел «Аналитика») — то же число, что и «Репутация» в списке выше, просто в динамике.</summary>
    public LineChartDiagram.ChartLayout BuildReputationChartLayout()
    {
        var series = new[] { new LineChartDiagram.ChartSeries("Репутация", SectorColors.Palette[1], History!.ReputationByTurn) };

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 560, 220, value => value.ToString("0") + "%");
    }

    public MarkupString RenderStockChart(FactoryRow factory) => ChartRenderer.Render(BuildStockChartLayout(factory));

    public MarkupString RenderOutputChart(FactoryRow factory) => ChartRenderer.Render(BuildOutputChartLayout(factory));

    public void SelectRecipe(Ulid factoryId)
    {
        if (!SelectedRecipeIds.TryGetValue(factoryId, out var recipeId))
        {
            return;
        }

        RunAction(() => Host.Session!.SelectRecipe(TeamId, factoryId, recipeId));
    }

    public void SellToSystem(string materialId)
    {
        var volume = SellVolumes.GetValueOrDefault(materialId, 1m);
        RunAction(() => Host.Session!.SellToSystem(TeamId, materialId, volume));
    }

    /// <summary>Снимает заявку на продажу этого материала этого хода (SPEC §4, §5.4) — объявление 0 отменяет предыдущее.</summary>
    public void CancelSaleToSystem(string materialId)
    {
        RunAction(() => Host.Session!.SellToSystem(TeamId, materialId, 0m));
    }

    /// <summary>
    /// Предпросмотр продажи материала системе (запрос пользователя: «сразу видеть ставку, доход и
    /// прибыль»; с блока 11.10 — ещё и собственную просадку цены) — пересчитывается на лету из уже
    /// закешированных <see cref="Market"/>/<see cref="EconomyConfig"/>/
    /// <see cref="RealUnitCostByMaterialId"/> при каждом изменении объёма, см.
    /// <see cref="MarketSalePreview"/>.
    /// </summary>
    public MarketSalePreview.Result SellPreview(StockRow row)
    {
        var volume = SellVolumes.GetValueOrDefault(row.MaterialId, 1m);
        if (Market is null || EconomyConfig is null)
        {
            return new MarketSalePreview.Result(HasQuote: false, 0m, 0m, 0m, 0m, 0m, 0m);
        }

        return MarketSalePreview.Calculate(
            Market, MaterialCosts, EconomyConfig, row.Material, volume,
            RealUnitCostByMaterialId.GetValueOrDefault(row.MaterialId),
            Host.Session!.Entries, Host.Session!.State.CurrentTurn);
    }

    public void ConfirmContract(Ulid contractId) =>
        RunAction(() => Host.Session!.ConfirmContract(contractId, TeamRole.Manager, TeamId));

    public void WithdrawProposal(Ulid proposalId) =>
        RunAction(() => Host.Session!.WithdrawContractProposal(proposalId, TeamId));

    public void RejectProposal(Ulid proposalId) =>
        RunAction(() => Host.Session!.RejectContractProposal(proposalId, TeamId));

    public void TerminateMutual(Ulid contractId) =>
        RunAction(() => Host.Session!.TerminateContract(contractId, ContractTerminationReason.Mutual, terminatingTeamId: null));

    public void TerminateVoluntary(Ulid contractId) =>
        RunAction(() => Host.Session!.TerminateContract(contractId, ContractTerminationReason.Voluntary, TeamId));

    public void ProposeRevision(Ulid contractId)
    {
        var volume = RevisionVolumes.GetValueOrDefault(contractId, 1m);
        var unitPrice = RevisionUnitPrices.GetValueOrDefault(contractId, 1m);
        var penaltyRate = RevisionPenaltyRates.GetValueOrDefault(contractId, 0.1m);
        var endTurn = RevisionEndTurns.GetValueOrDefault(contractId, CurrentTurn);
        RunAction(() => Host.Session!.ProposeContractRevision(contractId, TeamId, volume, unitPrice, penaltyRate, endTurn));
    }

    public void AcceptRevision(Ulid contractId) =>
        RunAction(() => Host.Session!.RespondToContractRevision(contractId, TeamRole.Manager, accept: true, Random.Shared));

    public void RejectRevision(Ulid contractId) =>
        RunAction(() => Host.Session!.RespondToContractRevision(contractId, TeamRole.Manager, accept: false, Random.Shared));

    /// <summary>
    /// Тот же эффективный множитель, что посчитает <see cref="Game.Engine.GameSession.EmergencyPurchase"/>
    /// — базовый плюс надбавка за недавние экстренные закупки этой команды именно этого материала
    /// (запрос пользователя: наказывать зависимость от рынка, а не саму операцию) — здесь только для
    /// предпросмотра до отправки действия.
    /// </summary>
    public decimal EmergencyPurchaseEffectiveMultiplier
    {
        get
        {
            if (EmergencyPurchaseMaterialId is null || EconomyConfig is null || Host.Session is null)
            {
                return 0m;
            }

            var recentVolume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(
                Host.Session.Entries, TeamId, EmergencyPurchaseMaterialId, CurrentTurn, EconomyConfig);
            return EconomyConfig.EmergencyPurchaseBaseMultiplier + EconomyConfig.EmergencyPurchasePressureMultiplierPerUnit * recentVolume;
        }
    }

    /// <summary>
    /// Та же цена, что посчитает <see cref="Game.Engine.GameSession.EmergencyPurchase"/> — рыночная
    /// котировка × эффективный множитель (см. <see cref="EmergencyPurchaseEffectiveMultiplier"/>) —
    /// здесь только для предпросмотра стоимости и проверки баланса до отправки действия (тот же
    /// приём, что <see cref="CanAffordSelectedBuild"/>).
    /// </summary>
    public decimal EmergencyPurchaseCost
    {
        get
        {
            if (EmergencyPurchaseMaterialId is null || EconomyConfig is null
                || !MaterialCosts.TryGetValue(EmergencyPurchaseMaterialId, out var unitCost))
            {
                return 0m;
            }

            var unitPrice = unitCost * EmergencyPurchaseEffectiveMultiplier;
            return unitPrice * EmergencyPurchaseVolume;
        }
    }

    /// <summary>Не блокирует покупку (баланс может свободно уйти в минус, docs/TODO.md #23) — только управляет информационной подсказкой рядом с кнопкой «Купить».</summary>
    public bool CanAffordEmergencyPurchase => EmergencyPurchaseCost <= Balance;

    public void EmergencyPurchase()
    {
        if (EmergencyPurchaseMaterialId is null)
        {
            return;
        }

        RunAction(() => Host.Session!.EmergencyPurchase(TeamId, EmergencyPurchaseMaterialId, EmergencyPurchaseVolume));
    }

    /// <summary>Снимает заявку на аварийную закупку выбранного материала этого хода (SPEC §4, §5.3) — объявление 0 отменяет предыдущее.</summary>
    public void CancelEmergencyPurchase()
    {
        if (EmergencyPurchaseMaterialId is null)
        {
            return;
        }

        RunAction(() => Host.Session!.EmergencyPurchase(TeamId, EmergencyPurchaseMaterialId, 0m));
    }

    public void SetRndCommitment(Ulid factoryId)
    {
        var amount = RndCommitmentAmounts.GetValueOrDefault(factoryId, 0m);
        RunAction(() => Host.Session!.SetRndCommitment(TeamId, factoryId, amount));
    }

    public void RequestOverhaul(Ulid factoryId)
    {
        RunAction(() => Host.Session!.SetOverhaulRequested(TeamId, factoryId, requested: true));
    }

    public void CancelOverhaulRequest(Ulid factoryId)
    {
        RunAction(() => Host.Session!.SetOverhaulRequested(TeamId, factoryId, requested: false));
    }

    public void SetGenerationResearchCommitment()
    {
        RunAction(() => Host.Session!.SetGenerationResearchCommitment(TeamId, GenerationResearchCommitmentInput));
    }

    public void SetAllocationShare(Ulid factoryId)
    {
        var share = AllocationShares.GetValueOrDefault(factoryId, 1m);
        RunAction(() => Host.Session!.SetFactoryAllocationShare(TeamId, factoryId, share));
    }

    /// <summary>
    /// Управляющий может входить и собирать команду ещё до старта сессии (см. Refresh) —
    /// значит, добавление переговорщика обязано уметь работать в обоих состояниях: до старта это
    /// черновик (<see cref="GameSessionHost.AddStagedParticipant"/>, тот же путь, что и у
    /// управляющего на /admin/teams), после — обычная регистрация в живую сессию
    /// (<see cref="GameSessionHost.RegisterParticipant"/>). Раньше здесь был общий <c>RunAction</c>,
    /// который требует живую сессию для любого действия, — это как раз и была причина «Сессия
    /// сейчас не активна» при попытке добавить кого-то до старта.
    /// </summary>
    public void AddTeamMember()
    {
        if (string.IsNullOrWhiteSpace(NewMemberName))
        {
            return;
        }

        var name = NewMemberName.Trim();
        ErrorMessage = null;
        try
        {
            lock (Host.SyncRoot)
            {
                if (Host.Session is null)
                {
                    Host.AddStagedParticipant(ParticipantRole.Negotiator, TeamId, name);
                }
                else
                {
                    Host.RegisterParticipant(ParticipantRole.Negotiator, TeamId, name);
                }
            }
            Refresh();
            NewMemberName = string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = ex.Message;
        }

        Changed?.Invoke();
    }

    public string QrLink(TeamMemberRow member)
    {
        var teamName = TeamName ?? Host.StagedTeams.FirstOrDefault(t => t.Id == TeamId)?.Name ?? "?";
        return $"/admin/qr/{member.Code}?name={Uri.EscapeDataString($"{member.DisplayName} — {RoleDisplay.Name(member.Role)} команды «{teamName}»")}";
    }

    public void RunAction(Action action)
    {
        ErrorMessage = null;
        try
        {
            lock (Host.SyncRoot)
            {
                if (Host.Session is null)
                {
                    ErrorMessage = "Сессия сейчас не активна.";
                    return;
                }

                action();
            }
            Refresh();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = ex.Message;
        }

        Changed?.Invoke();
    }
}
