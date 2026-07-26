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
    private readonly EventLog<GameSessionState> _log;

    /// <summary>Живое состояние сессии.</summary>
    public GameSessionState State => _log.State;

    /// <summary>Полная история событий сессии.</summary>
    public IReadOnlyList<EventLogEntry<GameSessionState>> Entries => _log.Entries;

    /// <summary>Оборачивает уже существующий журнал (например, восстановленный durable-слоем).</summary>
    public GameSession(EventLog<GameSessionState> log)
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

    /// <summary>Начинает сессию с уже известным ходом окончания (например, для тестов).</summary>
    public static GameSession StartWithEndTurn(
        ResolvedGameConfig config,
        string presetId,
        int endTurn,
        IReadOnlyList<TeamSpec> teams,
        JsonSerializerOptions? serializerOptions = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(teams);

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

        var log = new EventLog<GameSessionState>(new GameSessionState(config), serializerOptions, clock);
        log.Append(new SessionStarted { Id = Ulid.NewUlid(), PresetId = presetId, EndTurn = endTurn, Teams = teams });

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
            Reason = reason,
            TerminatingTeamId = reason == ContractTerminationReason.Voluntary ? terminatingTeamId : null,
            Fee = fee,
        });
    }

    /// <summary>
    /// Аварийная закупка материала у системы (SPEC §5.3): цена — системная цена материала ×
    /// множитель, служит потолком монопольных цен. Требует включённого флага и фазы решений.
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
        if (!State.Config.Materials.ContainsKey(materialId))
        {
            throw new ArgumentException($"Unknown material '{materialId}'.", nameof(materialId));
        }

        var systemPrice = State.Config.Raw.Economy.SystemPricePerMaterial
            .FirstOrDefault(price => price.MaterialId == materialId)
            ?? throw new InvalidOperationException($"No system price configured for material '{materialId}'.");
        var unitPrice = systemPrice.Price * State.Config.Raw.Economy.EmergencyPurchasePriceMultiplier;

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

    private Contract GetContract(Ulid contractId)
    {
        if (!State.Contracts.TryGetValue(contractId, out var contract))
        {
            throw new ArgumentException($"Unknown contract '{contractId}'.", nameof(contractId));
        }

        return contract;
    }

    /// <summary>
    /// Прогоняет расчёт одного тика в фиксированном порядке (SPEC §4): для всех команд финансы →
    /// производство снизу вверх по уровню материала, затем исполнение контрактов. События
    /// дописываются в журнал сразу по мере расчёта — не собираются заранее единым списком, — чтобы
    /// фабрика более высокого уровня видела в складе выход нижней в этом же тике, а последующая
    /// поставка — склад после предыдущей. Рынок/новости — заглушки (Фаза 6 ещё не реализована). Не
    /// вызывается автоматически при переходе фаз — таймер-driven вызов появится с real-time слоем
    /// (Блок 8.2).
    /// </summary>
    public IReadOnlyList<EventLogEntry<GameSessionState>> RunTick()
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
            foreach (var change in TickFinanceStep.Run(team, config.Raw.StartingConditions, config.Raw.WorkerProductivity))
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
                appended.Add(_log.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = contract.Id }));
            }
            else
            {
                var penalty = terms.Volume * terms.UnitPrice * terms.PenaltyRate;
                appended.Add(_log.Append(new DeliveryMissed
                {
                    Id = Ulid.NewUlid(),
                    ContractId = contract.Id,
                    ShortfallVolume = terms.Volume,
                    PenaltyAmount = penalty,
                }));
            }
        }
    }
}
