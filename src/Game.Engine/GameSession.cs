using System.Text.Json;
using Game.Config.Loading;
using Game.Config.Session;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Один прогон игры (AGENTS §2, правило 4 — состояние экземпляра, не статика; §5, терминология).
/// Тонкая обёртка над <see cref="EventLog{TState}"/> для <see cref="GameSessionState"/>: переводит
/// намерения («продлить фазу», «поставить на паузу») в конкретные <see cref="Change{TState}"/> и
/// проверяет бизнес-правила до записи в журнал — история, однажды воспроизводимая заново, не должна
/// сама бросать исключения валидации (AGENTS §2, правило 6).
/// </summary>
public sealed class GameSession
{
    private readonly IEventLog<GameSessionState> _log;

    /// <summary>Живое состояние сессии.</summary>
    public GameSessionState State => _log.State;

    /// <summary>Полная история событий сессии.</summary>
    public IReadOnlyList<EventLogEntry<GameSessionState>> Entries => _log.Entries;

    /// <summary>
    /// Оборачивает уже существующий журнал — как обычный <see cref="EventLog{TState}"/> (тесты,
    /// боты), так и <c>DurableEventLog</c> из Game.Persistence, восстановленный после сбоя (Блок 8.1)
    /// — <see cref="GameSession"/> одинаково работает с любой реализацией <see cref="IEventLog{TState}"/>.
    /// </summary>
    public GameSession(IEventLog<GameSessionState> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>
    /// Начинает новую сессию: разыгрывает ход окончания в диапазоне пресета и пишет об этом и о
    /// составе команд первую запись в журнал. Сессия сразу открывается в фазе расчёта первого хода.
    /// </summary>
    public static GameSession Start(
        ResolvedGameConfig config,
        SessionPresetConfig preset,
        IReadOnlyList<TeamSpec> teams,
        Random endTurnRandom,
        JsonSerializerOptions? serializerOptions = null,
        Func<DateTimeOffset>? clock = null)
    {
        var endTurn = SessionEndTurnDraw.Draw(preset, endTurnRandom);
        return StartWithEndTurn(config, preset.Id, endTurn, teams, serializerOptions, clock);
    }

    /// <summary>Начинает сессию с уже известным ходом окончания (например, для тестов), заводя собственный in-memory журнал.</summary>
    public static GameSession StartWithEndTurn(
        ResolvedGameConfig config,
        string presetId,
        int endTurn,
        IReadOnlyList<TeamSpec> teams,
        JsonSerializerOptions? serializerOptions = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var log = new EventLog<GameSessionState>(new GameSessionState(config), serializerOptions, clock);
        return StartWithEndTurn(log, presetId, endTurn, teams);
    }

    /// <summary>
    /// Начинает новую сессию поверх уже открытого, но ещё пустого журнала (Блок 8.1: например,
    /// свежеоткрытый durable-журнал из Game.Persistence, в котором ещё нет ни одной прежней записи)
    /// — та же логика, что и у перегрузки с <see cref="ResolvedGameConfig"/>, но без создания
    /// собственного <see cref="EventLog{TState}"/>.
    /// </summary>
    public static GameSession StartWithEndTurn(
        IEventLog<GameSessionState> log, string presetId, int endTurn, IReadOnlyList<TeamSpec> teams)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(teams);

        var config = log.State.Config;
        foreach (var spec in teams)
        {
            if (spec.StartingLoanAmount > config.Raw.StartingConditions.MaxStartingLoanAmount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(teams), spec.StartingLoanAmount,
                    $"Team '{spec.Id}' requested a starting loan above the configured maximum " +
                    $"of {config.Raw.StartingConditions.MaxStartingLoanAmount}.");
            }
        }

        log.Append(new SessionStarted
        {
            Id = Ulid.NewUlid(),
            PresetId = presetId,
            EndTurn = endTurn,
            ConfigHash = config.ContentHash,
            Teams = teams,
        });

        return new GameSession(log);
    }

    /// <summary>
    /// Переводит сессию к следующей фазе (или к следующему ходу, если завершалась фаза «завершение»),
    /// либо, если текущий ход — <see cref="GameSessionState.EndTurn"/>, помечает сессию завершённой.
    /// </summary>
    public EventLogEntry<GameSessionState> AdvancePhase(PhaseTransitionTrigger trigger)
    {
        if (State.IsFinished)
        {
            throw new InvalidOperationException("Cannot advance the phase of a session that has already finished.");
        }

        return _log.Append(new PhaseAdvanced { Id = Ulid.NewUlid(), Trigger = trigger });
    }

    /// <summary>Продлевает текущую фазу на <paramref name="by"/>.</summary>
    public EventLogEntry<GameSessionState> ExtendCurrentPhase(TimeSpan by)
    {
        if (by <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), by, "Phase extension must be positive.");
        }
        if (State.IsFinished)
        {
            throw new InvalidOperationException("Cannot extend a phase of a session that has already finished.");
        }

        return _log.Append(new PhaseExtended { Id = Ulid.NewUlid(), By = by });
    }

    /// <summary>Ставит сессию на паузу.</summary>
    public EventLogEntry<GameSessionState> Pause()
    {
        if (State.IsPaused)
        {
            throw new InvalidOperationException("Session is already paused.");
        }
        if (State.IsFinished)
        {
            throw new InvalidOperationException("Cannot pause a session that has already finished.");
        }

        return _log.Append(new SessionPaused { Id = Ulid.NewUlid() });
    }

    /// <summary>Снимает сессию с паузы.</summary>
    public EventLogEntry<GameSessionState> Resume()
    {
        if (!State.IsPaused)
        {
            throw new InvalidOperationException("Session is not paused.");
        }

        return _log.Append(new SessionResumed { Id = Ulid.NewUlid() });
    }

    /// <summary>
    /// Бросает, если решения команд сейчас недопустимы (любая фаза, кроме
    /// <see cref="TurnPhase.Decision"/>) — фазы расчёта и завершения read-only (SPEC §4).
    /// Действия команд (контракты, производство и т.д. из следующих блоков) обязаны вызывать этот
    /// метод перед записью своих событий.
    /// </summary>
    public void EnsureDecisionsAllowed()
    {
        if (State.CurrentPhase != TurnPhase.Decision)
        {
            throw new InvalidOperationException(
                $"Team decisions are not allowed during the '{State.CurrentPhase}' phase.");
        }
    }

    /// <summary>Проверяет целостность хеш-цепочки журнала сессии.</summary>
    public bool VerifyIntegrity() => _log.VerifyIntegrity();

    /// <summary>
    /// Публичная репутация команды на данный момент (Блок 6.2, SPEC §7) — пересчитывается по
    /// журналу заново при каждом обращении, не хранится как отдельное состояние.
    /// </summary>
    public ReputationResult GetReputation(Ulid teamId) =>
        ReputationCalculator.Calculate(Entries, State.Contracts, teamId, State.CurrentTurn, State.Config.Raw.Reputation);

    /// <summary>
    /// Сводит две независимо поданные заявки в контракт (SPEC §6). При совпадении — записывает
    /// подписанный контракт (статус «ждёт подтверждения») в журнал; при конфликте ничего не пишет и
    /// возвращает список того, что разошлось. Заключать сделки можно только в фазе решений.
    /// </summary>
    public ContractFormationResult SubmitContractProposals(
        ContractProposal proposalA, ContractProposal proposalB, Random confirmationCodeRandom)
    {
        EnsureDecisionsAllowed();

        var result = ContractFormation.TryMatch(proposalA, proposalB, Ulid.NewUlid(), confirmationCodeRandom);
        if (result.IsMatched)
        {
            _log.Append(new ContractSigned { Id = Ulid.NewUlid(), Contract = ContractSpec.From(result.Contract!) });
        }

        return result;
    }

    /// <summary>Финальное подтверждение сделки управляющим (SPEC §3, §6). Только в фазе решений.</summary>
    public EventLogEntry<GameSessionState> ConfirmContract(Ulid contractId, TeamRole confirmingRole)
    {
        EnsureDecisionsAllowed();

        if (confirmingRole != TeamRole.Manager)
        {
            throw new InvalidOperationException("Only a team manager can give the final confirmation of a contract.");
        }

        var contract = GetContract(contractId);
        if (contract.Status != ContractStatus.PendingConfirmation)
        {
            throw new InvalidOperationException($"Cannot confirm a contract in status '{contract.Status}'.");
        }

        return _log.Append(new ContractConfirmed { Id = Ulid.NewUlid(), ContractId = contractId });
    }

    /// <summary>
    /// Прекращает действующий контракт (SPEC §6): mutual — без платы; voluntary — инициатор платит
    /// фиксированную плату из конфига. Только в фазе решений.
    /// </summary>
    public EventLogEntry<GameSessionState> TerminateContract(
        Ulid contractId, ContractTerminationReason reason, Ulid? terminatingTeamId)
    {
        EnsureDecisionsAllowed();

        var contract = GetContract(contractId);
        if (contract.Status != ContractStatus.Active)
        {
            throw new InvalidOperationException($"Cannot terminate a contract in status '{contract.Status}'.");
        }

        decimal fee = 0m;
        if (reason == ContractTerminationReason.Voluntary)
        {
            if (terminatingTeamId is not { } initiator || (initiator != contract.BuyerTeamId && initiator != contract.SellerTeamId))
            {
                throw new ArgumentException(
                    "Voluntary termination requires the initiating team to be a party to the contract.", nameof(terminatingTeamId));
            }

            fee = State.Config.Raw.Contracts.VoluntaryTerminationFee;
        }

        return _log.Append(new ContractTerminated
        {
            Id = Ulid.NewUlid(),
            ContractId = contractId,
            Turn = State.CurrentTurn,
            Reason = reason,
            TerminatingTeamId = reason == ContractTerminationReason.Voluntary ? terminatingTeamId : null,
            Fee = fee,
        });
    }

    /// <summary>
    /// Аварийная закупка материала у системы (SPEC §5.3): цена — текущая рыночная котировка
    /// материала (Блок 6.1) × множитель, служит потолком монопольных цен. Требует включённого
    /// флага и фазы решений.
    /// </summary>
    public EventLogEntry<GameSessionState> EmergencyPurchase(Ulid teamId, string materialId, decimal volume)
    {
        EnsureDecisionsAllowed();

        if (!State.Config.Raw.FeatureFlags.EmergencyPurchaseEnabled)
        {
            throw new InvalidOperationException("Emergency purchase is disabled in this session.");
        }
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Emergency purchase volume must be positive.");
        }
        if (!State.Teams.ContainsKey(teamId))
        {
            throw new ArgumentException($"Unknown team '{teamId}'.", nameof(teamId));
        }

        var unitPrice = GetQuoteOrThrow(materialId).Price * State.Config.Raw.Economy.EmergencyPurchasePriceMultiplier;

        return _log.Append(new EmergencyPurchased
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            MaterialId = materialId,
            Volume = volume,
            UnitPrice = unitPrice,
            TotalCost = unitPrice * volume,
        });
    }

    /// <summary>
    /// Продажа материала (любого уровня передела) системе по рыночной цене (Блок 6.1, SPEC §5.4):
    /// в пределах оставшейся на этот ход ёмкости — по полной цене с множителем маржи передела,
    /// сверх — с понижающим коэффициентом. Требует фазы решений.
    /// </summary>
    public EventLogEntry<GameSessionState> SellToSystem(Ulid teamId, string materialId, decimal volume)
    {
        EnsureDecisionsAllowed();

        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Sale volume must be positive.");
        }
        if (!State.Teams.TryGetValue(teamId, out var team))
        {
            throw new ArgumentException($"Unknown team '{teamId}'.", nameof(teamId));
        }
        if (!State.Config.Materials.TryGetValue(materialId, out var material))
        {
            throw new ArgumentException($"Unknown material '{materialId}'.", nameof(materialId));
        }
        GetQuoteOrThrow(materialId);

        var available = team.Warehouse.QuantityOf(material);
        if (available < volume)
        {
            throw new InvalidOperationException(
                $"Team '{teamId}' cannot sell {volume} of '{materialId}': only {available} in stock.");
        }

        var sale = MarketSaleCalculator.Calculate(State.Market, State.Config.Raw.Economy, material, volume);

        return _log.Append(new MaterialSoldToSystem
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            MaterialId = materialId,
            Volume = volume,
            WithinCapacityVolume = sale.WithinCapacityVolume,
            OverflowVolume = sale.OverflowVolume,
            UnitPrice = sale.UnitPrice,
            TotalRevenue = sale.TotalRevenue,
        });
    }

    /// <summary>
    /// Строит фабрику заданного типа для команды (SPEC §5.6, Блок 7.1): постройка мгновенная —
    /// фабрика естественным образом начинает работать со следующего хода, отдельного «отложенного»
    /// состояния не требуется, так как ближайший расчёт тика уже увидит её в составе команды.
    /// Фабрика без рабочих ничего не производит — наём отдельным действием (<see cref="HireWorkers"/>).
    /// Требует фазы решений.
    /// </summary>
    public EventLogEntry<GameSessionState> BuildFactory(Ulid teamId, string factoryDefinitionId, string? recipeId = null)
    {
        EnsureDecisionsAllowed();

        var team = GetTeam(teamId);
        var definition = State.Config.FactoryDefinitions.FirstOrDefault(f => f.Id == factoryDefinitionId);
        if (definition is null)
        {
            throw new ArgumentException($"Unknown factory definition '{factoryDefinitionId}'.", nameof(factoryDefinitionId));
        }
        if (definition.Sector != team.Sector)
        {
            throw new ArgumentException(
                $"Factory definition '{factoryDefinitionId}' belongs to sector '{definition.Sector.Id}', " +
                $"not team's sector '{team.Sector.Id}'.",
                nameof(factoryDefinitionId));
        }

        var recipe = recipeId is null
            ? definition.Recipes[0]
            : definition.Recipes.FirstOrDefault(r => r.Id == recipeId);
        if (recipe is null)
        {
            throw new ArgumentException(
                $"Recipe '{recipeId}' is not produced by factory definition '{factoryDefinitionId}'.", nameof(recipeId));
        }

        var cost = State.Config.Raw.FactoryDefinitions.First(f => f.Id == factoryDefinitionId).BuildCost;

        return _log.Append(new FactoryBuilt
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            FactoryId = Ulid.NewUlid(),
            FactoryDefinitionId = factoryDefinitionId,
            RecipeId = recipe.Id,
            Cost = cost,
        });
    }

    /// <summary>Нанимает рабочих на фабрику (SPEC §5.6: наём мгновенный, с разовой платой за действие). Требует фазы решений.</summary>
    public EventLogEntry<GameSessionState> HireWorkers(Ulid teamId, Ulid factoryId, int count)
    {
        EnsureDecisionsAllowed();

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Hire count must be positive.");
        }
        var team = GetTeam(teamId);
        GetFactory(team, factoryId);

        var cost = count * State.Config.Raw.WorkerProductivity.HireCostPerWorker;

        return _log.Append(new WorkersHired
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            FactoryId = factoryId,
            Count = count,
            Cost = cost,
        });
    }

    /// <summary>Увольняет рабочих с фабрики (SPEC §5.6: увольнение мгновенное, с разовой платой за действие). Требует фазы решений.</summary>
    public EventLogEntry<GameSessionState> FireWorkers(Ulid teamId, Ulid factoryId, int count)
    {
        EnsureDecisionsAllowed();

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Fire count must be positive.");
        }
        var team = GetTeam(teamId);
        var factory = GetFactory(team, factoryId);
        if (count > factory.Workers)
        {
            throw new InvalidOperationException($"Cannot fire {count} workers, factory '{factoryId}' only has {factory.Workers}.");
        }

        var cost = count * State.Config.Raw.WorkerProductivity.FireCostPerWorker;

        return _log.Append(new WorkersFired
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            FactoryId = factoryId,
            Count = count,
            Cost = cost,
        });
    }

    /// <summary>
    /// Ручное событие ведущего (SPEC §9.5): публикует конкретный заголовок из библиотеки, минуя
    /// автоматический подбор по тренду (Блок 6.3) — тем же событием <see cref="NewsPublished"/> и с
    /// тем же ограничением на повтор, так что использованный вручную заголовок больше никогда не
    /// прозвучит, включая автоматический подбор следующих ходов. Не привязано к фазе решений — это
    /// действие ведущего, а не команды.
    /// </summary>
    public EventLogEntry<GameSessionState> PublishManualNews(string newsItemId)
    {
        var item = State.Config.Raw.News.FirstOrDefault(candidate => candidate.Id == newsItemId);
        if (item is null)
        {
            throw new ArgumentException($"Unknown news item '{newsItemId}'.", nameof(newsItemId));
        }
        if (State.NewsFeed.IsPublished(newsItemId))
        {
            throw new InvalidOperationException($"News item '{newsItemId}' has already been published this session.");
        }

        return _log.Append(new NewsPublished
        {
            Id = Ulid.NewUlid(),
            Turn = State.CurrentTurn,
            NewsItemId = item.Id,
            Trend = item.Trend,
            Headline = item.Headline,
        });
    }

    /// <summary>
    /// Регистрирует участника сессии под свежим кодом входа (Блок 8.1, SPEC §3): управляющий и
    /// переговорщик обязаны быть привязаны к существующей команде, остальные роли — обязаны не быть
    /// привязаны ни к какой. Не привязано к фазе решений — это настройка, а не игровое решение.
    /// </summary>
    public EventLogEntry<GameSessionState> RegisterParticipant(
        ParticipantRole role, Ulid? teamId, string displayName, Random codeRandom)
    {
        ArgumentNullException.ThrowIfNull(codeRandom);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name must not be empty.", nameof(displayName));
        }

        var isTeamScoped = role is ParticipantRole.Manager or ParticipantRole.Negotiator;
        if (isTeamScoped)
        {
            if (teamId is null)
            {
                throw new ArgumentException($"Role '{role}' requires a team.", nameof(teamId));
            }
            GetTeam(teamId.Value);
        }
        else if (teamId is not null)
        {
            throw new ArgumentException($"Role '{role}' must not be bound to a team.", nameof(teamId));
        }

        string code;
        do
        {
            code = ShortCode.Generate(codeRandom);
        }
        while (State.Participants.ContainsKey(code));

        return _log.Append(new ParticipantRegistered
        {
            Id = Ulid.NewUlid(),
            Code = code,
            Role = role,
            TeamId = teamId,
            DisplayName = displayName,
        });
    }

    /// <summary>Ищет зарегистрированного участника по коду входа; null, если код не зарегистрирован.</summary>
    public ParticipantRegistration? TryAuthenticate(string code) =>
        State.Participants.GetValueOrDefault(code);

    /// <summary>Текущая рыночная котировка материала или внятная ошибка, если рынок ещё не публиковал её.</summary>
    private MaterialQuote GetQuoteOrThrow(string materialId)
    {
        if (!State.Config.Materials.ContainsKey(materialId))
        {
            throw new ArgumentException($"Unknown material '{materialId}'.", nameof(materialId));
        }
        if (!State.Market.HasQuote(materialId))
        {
            throw new InvalidOperationException($"No market quote available yet for material '{materialId}'.");
        }

        return State.Market.QuoteOf(materialId);
    }

    private Contract GetContract(Ulid contractId)
    {
        if (!State.Contracts.TryGetValue(contractId, out var contract))
        {
            throw new ArgumentException($"Unknown contract '{contractId}'.", nameof(contractId));
        }

        return contract;
    }

    private Team GetTeam(Ulid teamId)
    {
        if (!State.Teams.TryGetValue(teamId, out var team))
        {
            throw new ArgumentException($"Unknown team '{teamId}'.", nameof(teamId));
        }

        return team;
    }

    private static Factory GetFactory(Team team, Ulid factoryId)
    {
        var factory = team.Factories.FirstOrDefault(f => f.Id == factoryId);
        if (factory is null)
        {
            throw new ArgumentException($"Team '{team.Id}' has no factory '{factoryId}'.", nameof(factoryId));
        }

        return factory;
    }

    /// <summary>
    /// Прогоняет расчёт одного тика в фиксированном порядке (SPEC §4): для всех команд финансы (по
    /// репутации, накопленной за все предыдущие ходы, — Блок 6.2) → производство снизу вверх по
    /// уровню материала, затем исполнение контрактов, затем обновление рынка (Блок 6.1), затем
    /// новости по тренду (Блок 6.3) — оба публикуются даже без единой команды в сессии, они не
    /// зависят от них. События дописываются в журнал сразу по мере расчёта — не собираются заранее
    /// единым списком, — чтобы фабрика более высокого уровня видела в складе выход нижней в этом же
    /// тике, а последующая поставка — склад после предыдущей, и (для финансов) чтобы собственные
    /// срывы/расторжения этого же хода не успевали ударить по ставке, начисленной в его начале.
    /// <paramref name="newsRandom"/> — случайность подбора заголовка (AGENTS §2, правило 6:
    /// никакой случайности без явного, при необходимости засеянного, экземпляра); если пул
    /// заголовков текущего тренда в этой сессии исчерпан, новости в этот ход не будет. Не
    /// вызывается автоматически при переходе фаз — таймер-driven вызов появится с real-time слоем
    /// (Блок 8.2).
    /// </summary>
    public IReadOnlyList<EventLogEntry<GameSessionState>> RunTick(Random newsRandom)
    {
        if (State.CurrentPhase != TurnPhase.Calculation)
        {
            throw new InvalidOperationException(
                $"Cannot run a tick outside the '{TurnPhase.Calculation}' phase (currently '{State.CurrentPhase}').");
        }

        var appended = new List<EventLogEntry<GameSessionState>>();
        var config = State.Config;

        foreach (var team in State.Teams.Values.OrderBy(team => team.Id))
        {
            var reputation = GetReputation(team.Id);
            foreach (var change in TickFinanceStep.Run(team, config.Raw.StartingConditions, config.Raw.WorkerProductivity, reputation.Percentage))
            {
                appended.Add(_log.Append(change));
            }

            foreach (var factory in team.Factories.OrderBy(f => f.SelectedRecipe.Output.Level).ThenBy(f => f.Id))
            {
                var result = ProductionCalculator.Calculate(
                    factory, team.Warehouse, config.Raw.WorkerProductivity, config.Raw.Rnd);

                appended.Add(_log.Append(new FactoryProduced
                {
                    Id = Ulid.NewUlid(),
                    TeamId = team.Id,
                    FactoryId = result.FactoryId,
                    CapacityLimitedOutputQuantity = result.CapacityLimitedOutputQuantity,
                    OutputQuantity = result.OutputQuantity,
                    ConsumedInputs = result.ConsumedInputs,
                }));
            }
        }

        ExecuteContracts(appended);

        var marketUpdate = MarketCalculator.Calculate(State.CurrentTurn, config.Raw.Economy);
        appended.Add(_log.Append(new MarketUpdated
        {
            Id = Ulid.NewUlid(),
            Quotes = marketUpdate.Quotes,
            ElectricityPrice = marketUpdate.ElectricityPrice,
        }));

        var currentTrend = NewsCalculator.CurrentTrend(State.CurrentTurn, config.Raw.Economy.TrendScenario);
        var nextNews = NewsCalculator.SelectNext(config.Raw.News, State.NewsFeed, currentTrend, newsRandom);
        if (nextNews is not null)
        {
            appended.Add(_log.Append(new NewsPublished
            {
                Id = Ulid.NewUlid(),
                Turn = State.CurrentTurn,
                NewsItemId = nextNews.Id,
                Trend = nextNews.Trend,
                Headline = nextNews.Headline,
            }));
        }

        return appended;
    }

    /// <summary>
    /// Исполнение контрактов, у которых на текущем ходу положена поставка (SPEC §6). Контракты
    /// перебираются в детерминированном порядке (по идентификатору, не по порядку словаря — AGENTS
    /// §2, правило 6); по каждому решается, обеспечена ли поставка складом продавца — успех или
    /// Delivery Miss, — и событие дописывается сразу, чтобы последующие поставки видели уже
    /// обновлённые склады.
    /// </summary>
    private void ExecuteContracts(List<EventLogEntry<GameSessionState>> appended)
    {
        var currentTurn = State.CurrentTurn;
        var dueContracts = State.Contracts.Values
            .Where(contract => ContractExecution.IsDeliveryDue(contract, currentTurn))
            .OrderBy(contract => contract.Id)
            .ToList();

        foreach (var contract in dueContracts)
        {
            var terms = contract.Terms;
            var seller = State.Teams[contract.SellerTeamId];
            var available = seller.Warehouse.QuantityOf(terms.Material);

            if (available >= terms.Volume)
            {
                appended.Add(_log.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = contract.Id, Turn = currentTurn }));
            }
            else
            {
                var penalty = terms.Volume * terms.UnitPrice * terms.PenaltyRate;
                appended.Add(_log.Append(new DeliveryMissed
                {
                    Id = Ulid.NewUlid(),
                    ContractId = contract.Id,
                    Turn = currentTurn,
                    ShortfallVolume = terms.Volume,
                    PenaltyAmount = penalty,
                }));
            }
        }
    }
}
