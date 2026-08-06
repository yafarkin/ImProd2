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
    /// <see cref="TurnPhase.Decision"/>) — фаза расчёта+завершения read-only (SPEC §4).
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
    /// Подтверждение сделки оператором по коду (Блок 9.5, SPEC §6, §9.4) — второй, равноправный
    /// путь к тому же результату, что и <see cref="ConfirmContract"/>. Только в фазе решений.
    /// </summary>
    public EventLogEntry<GameSessionState> ConfirmContractByOperator(Ulid contractId)
    {
        EnsureDecisionsAllowed();

        var contract = GetContract(contractId);
        if (contract.Status != ContractStatus.PendingConfirmation)
        {
            throw new InvalidOperationException($"Cannot confirm a contract in status '{contract.Status}'.");
        }

        return _log.Append(new ContractConfirmedByOperator { Id = Ulid.NewUlid(), ContractId = contractId });
    }

    /// <summary>Оператор отклоняет сделку на этапе подтверждения, с причиной (Блок 9.5, SPEC §9.4). Только в фазе решений.</summary>
    public EventLogEntry<GameSessionState> RejectContract(Ulid contractId, string reason)
    {
        EnsureDecisionsAllowed();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejection reason is required.", nameof(reason));
        }

        var contract = GetContract(contractId);
        if (contract.Status != ContractStatus.PendingConfirmation)
        {
            throw new InvalidOperationException($"Cannot reject a contract in status '{contract.Status}'.");
        }

        return _log.Append(new ContractRejected { Id = Ulid.NewUlid(), ContractId = contractId, Reason = reason.Trim() });
    }

    /// <summary>Ищет контракт по коду подтверждения (Блок 9.5, SPEC §9.4: «ввод/скан кода»); <c>null</c>, если код неизвестен.</summary>
    public Contract? FindContractByConfirmationCode(string code) =>
        State.Contracts.Values.FirstOrDefault(c => c.ConfirmationCode == code);

    /// <summary>
    /// Предлагает пересмотр условий действующего recurring-контракта (Блок 9.3, SPEC §6): вторая
    /// сторона вправе принять или отклонить через <see cref="RespondToContractRevision"/>, при
    /// отказе контракт продолжает действовать без изменений и без штрафа за сам факт предложения.
    /// Только в фазе решений.
    /// </summary>
    public EventLogEntry<GameSessionState> ProposeContractRevision(
        Ulid contractId, Ulid proposingTeamId, decimal volume, decimal unitPrice, decimal penaltyRate, int recurringEndTurn)
    {
        EnsureDecisionsAllowed();

        var contract = GetContract(contractId);
        if (contract.Status != ContractStatus.Active)
        {
            throw new InvalidOperationException($"Cannot propose a revision for a contract in status '{contract.Status}'.");
        }
        if (contract.Terms.Type != ContractType.Recurring)
        {
            throw new InvalidOperationException("Only recurring contracts can be revised.");
        }
        if (proposingTeamId != contract.BuyerTeamId && proposingTeamId != contract.SellerTeamId)
        {
            throw new ArgumentException(
                "The proposing team must be a party to the contract.", nameof(proposingTeamId));
        }
        if (ContractRevisionCalculator.FindPending(Entries, contractId) is not null)
        {
            throw new InvalidOperationException($"Contract '{contractId}' already has a pending revision proposal.");
        }

        // Конструктор ContractTerms уже валидирует диапазоны и recurringEndTurn >= effectiveTurn —
        // не дублируем эти проверки здесь (по прецеденту InvestInRnd, Блок 9.2).
        _ = new ContractTerms(
            ContractType.Recurring, contract.Terms.Material, volume, unitPrice, penaltyRate,
            contract.Terms.EffectiveTurn, spotDeliveryTurn: null, recurringEndTurn);

        return _log.Append(new ContractRevisionProposed
        {
            Id = Ulid.NewUlid(),
            ContractId = contractId,
            ProposingTeamId = proposingTeamId,
            Volume = volume,
            UnitPrice = unitPrice,
            PenaltyRate = penaltyRate,
            RecurringEndTurn = recurringEndTurn,
        });
    }

    /// <summary>
    /// Отвечает на висящее предложение пересмотра условий контракта (Блок 9.3, SPEC §6): та же роль,
    /// что и у финального подтверждения сделки (<see cref="ConfirmContract"/>) — принятие/отклонение
    /// пересмотра такое же по весу решение. При принятии старый контракт расторгается без штрафа
    /// (обе стороны уже согласились) и заводится новый, сразу действующий. Только в фазе решений.
    /// </summary>
    public EventLogEntry<GameSessionState> RespondToContractRevision(
        Ulid contractId, TeamRole respondingRole, bool accept, Random confirmationCodeRandom)
    {
        EnsureDecisionsAllowed();

        if (respondingRole != TeamRole.Manager)
        {
            throw new InvalidOperationException("Only a team manager can respond to a contract revision proposal.");
        }

        var contract = GetContract(contractId);
        var pending = ContractRevisionCalculator.FindPending(Entries, contractId)
            ?? throw new InvalidOperationException($"Contract '{contractId}' has no pending revision proposal.");

        ContractSpec? replacement = null;
        if (accept)
        {
            var newTerms = new ContractTerms(
                ContractType.Recurring, contract.Terms.Material, pending.Volume, pending.UnitPrice, pending.PenaltyRate,
                contract.Terms.EffectiveTurn, spotDeliveryTurn: null, pending.RecurringEndTurn);
            var code = ContractConfirmationCode.Generate(confirmationCodeRandom);
            var newContract = new Contract(
                Ulid.NewUlid(), contract.BuyerTeamId, contract.SellerTeamId, newTerms, code, supersedesContractId: contractId);
            replacement = ContractSpec.From(newContract); // Status подтверждается в Apply при восстановлении, см. ContractRevisionResolved
        }

        return _log.Append(new ContractRevisionResolved
        {
            Id = Ulid.NewUlid(),
            ContractId = contractId,
            Accepted = accept,
            ReplacementContract = replacement,
        });
    }

    /// <summary>
    /// Висящее предложение пересмотра условий контракта, если оно есть (Блок 9.3) — пересчитывается
    /// по журналу заново при каждом обращении, не хранится как отдельное состояние (см. <see cref="GetReputation"/>).
    /// </summary>
    public ContractRevisionProposed? GetPendingContractRevision(Ulid contractId) =>
        ContractRevisionCalculator.FindPending(Entries, contractId);

    /// <summary>
    /// Публикует запись на доске потребностей (Блок 9.4, SPEC §9.2): избыток или дефицит материала,
    /// грубый порядок объёма, необязательный комментарий. Осознанно без <see cref="EnsureDecisionsAllowed"/> —
    /// это не экономическое решение хода, а канал общения, который SPEC описывает как «живёт в
    /// телефонах... доступна всем» без привязки к фазе, в отличие от команд §5.x.
    /// </summary>
    public EventLogEntry<GameSessionState> PostNeed(
        Ulid teamId, string materialId, NeedDirection direction, NeedVolumeOrder volumeOrder, string? comment)
    {
        GetTeam(teamId);
        if (!State.Config.Materials.ContainsKey(materialId))
        {
            throw new ArgumentException($"Unknown material '{materialId}'.", nameof(materialId));
        }

        return _log.Append(new NeedPosted
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            NeedId = Ulid.NewUlid(),
            MaterialId = materialId,
            Direction = direction,
            VolumeOrder = volumeOrder,
            Comment = comment,
        });
    }

    /// <summary>Отзывает собственную запись команды с доски потребностей (Блок 9.4, SPEC §9.2). Без фазового гейта, как и <see cref="PostNeed"/>.</summary>
    public EventLogEntry<GameSessionState> WithdrawNeed(Ulid teamId, Ulid needId)
    {
        var need = GetNeed(needId);
        if (need.TeamId != teamId)
        {
            throw new ArgumentException("Only the posting team can withdraw its own posting.", nameof(teamId));
        }

        return _log.Append(new NeedWithdrawn { Id = Ulid.NewUlid(), NeedId = needId });
    }

    /// <summary>
    /// Аварийная закупка материала у системы (SPEC §5.3): цена — текущая рыночная котировка
    /// материала (Блок 6.1) × множитель, служит потолком монопольных цен. Требует включённого
    /// флага и фазы решений.
    /// </summary>
    public EventLogEntry<GameSessionState> EmergencyPurchase(Ulid teamId, string materialId, decimal volume)
    {
        EnsureDecisionsAllowed();

        if (!State.EmergencyPurchaseEnabled)
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

        var economy = State.Config.Raw.Economy;
        // Наказывает не саму операцию, а зависимость от неё (запрос пользователя): множитель растёт
        // сверх базового с недавним объёмом закупок именно этой команды именно этого материала и
        // затухает сам по себе через несколько ходов без таких закупок, см.
        // EmergencyPurchasePressureCalculator.
        var recentVolume = EmergencyPurchasePressureCalculator.CalculateRecentVolume(
            Entries, teamId, materialId, State.CurrentTurn, economy);
        var effectiveMultiplier = economy.EmergencyPurchaseBaseMultiplier
            + economy.EmergencyPurchasePressureMultiplierPerUnit * recentVolume;
        var unitPrice = GetQuoteOrThrow(materialId).Price * effectiveMultiplier;

        return _log.Append(new EmergencyPurchased
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            MaterialId = materialId,
            Volume = volume,
            UnitPrice = unitPrice,
            TotalCost = unitPrice * volume,
            Turn = State.CurrentTurn,
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
    /// Берёт заём по решению команды (SPEC §5.9: «в любой момент», ставка — кривая из
    /// <see cref="FinanceCalculator"/>) — единственный способ получить деньги, первый он для
    /// команды или очередной: никакого отдельного «стартового» кредита с иными правилами больше
    /// нет (команда сама решает, сколько и когда занять — это её первое финансовое решение в игре,
    /// а не предустановка администратора). Ничем не ограничен по сумме — риск команды
    /// самонаказывающийся через растущую ставку, а не через жёсткий потолок. Требует фазы решений.
    /// </summary>
    public EventLogEntry<GameSessionState> TakeLoan(Ulid teamId, decimal amount)
    {
        EnsureDecisionsAllowed();

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Loan amount must be positive.");
        }
        GetTeam(teamId);

        return _log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = teamId, Amount = amount });
    }

    /// <summary>
    /// Добровольно гасит часть тела долга сверх обязательного платежа, который и без того списывается
    /// каждый ход (<see cref="MandatoryLoanRepaymentCharged"/>) — симметричное действие к
    /// <see cref="TakeLoan"/>. Нельзя погасить больше, чем команда реально должна; в отличие от
    /// постройки фабрики или найма, отдельной проверки баланса здесь нет — если денег не хватит,
    /// баланс уйдёт в минус и это решит тот же тик, самым последним шагом (<see
    /// cref="ForcedLoanStep"/>), тем же способом, каким уже работают все остальные решения команды.
    /// <paramref name="amount"/> сверх реального остатка долга не отклоняется, а тихо урезается до
    /// него (баг-репорт пользователя: UI округляет долг для отображения — «1 ¤» вместо реальных
    /// 0.9966... — и попытка погасить ровно то, что показано на экране, раньше падала с исключением;
    /// команда явно имела в виду «закрыть долг полностью», а не какую-то конкретную копейку). Требует
    /// фазы решений. Бросает, только если долга вообще нет (гасить нечего) — это уже настоящая ошибка
    /// команды, а не следствие округления в UI.
    /// </summary>
    public EventLogEntry<GameSessionState> RepayLoan(Ulid teamId, decimal amount)
    {
        EnsureDecisionsAllowed();

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Repayment amount must be positive.");
        }
        var team = GetTeam(teamId);
        if (team.Debt <= 0)
        {
            throw new InvalidOperationException($"Cannot repay {amount}, team '{teamId}' has no debt.");
        }

        var repayAmount = Math.Min(amount, team.Debt);

        return _log.Append(new LoanRepaid { Id = Ulid.NewUlid(), TeamId = teamId, Amount = repayAmount });
    }

    /// <summary>
    /// Строит фабрику заданного типа для команды (SPEC §5.6, Блок 7.1): постройка мгновенная —
    /// фабрика естественным образом начинает работать со следующего хода, отдельного «отложенного»
    /// состояния не требуется, так как ближайший расчёт тика уже увидит её в составе команды.
    /// Фабрика без рабочих ничего не производит — наём отдельным действием (<see cref="SetWorkerCount"/>).
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

        // Тот же приём, что уже использует UI (Team.razor) для группировки по «уровню пирамиды» —
        // берём уровень материала-выхода первого рецепта фабрики (Блок 9.2, запрос пользователя:
        // будущие фабрики должны открываться постепенно, а не быть доступны с хода 1).
        var generation = definition.Recipes[0].Output.Level;
        if (generation > team.UnlockedGeneration)
        {
            throw new ArgumentException(
                $"Factory definition '{factoryDefinitionId}' requires generation {generation}, " +
                $"but team has only unlocked generation {team.UnlockedGeneration}.",
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

    /// <summary>
    /// Меняет желаемую численность рабочих фабрики на ближайший расчёт (SPEC §5.6, запрос
    /// пользователя: сколько бы раз команда ни передумала за ход, списать деньги только один раз) —
    /// само объявление бесплатно и мгновенно, тем же приёмом, что и <see cref="SetRndCommitment"/>:
    /// реальный наём/увольнение и разовая плата за него происходят один раз за ход, на фазе расчёта
    /// (см. <see cref="TickFinanceStep"/>, <see cref="WorkforceStep"/>), по итоговой разнице между
    /// объявленным и фактическим числом рабочих на тот момент. Требует фазы решений. Бросает <see
    /// cref="ArgumentOutOfRangeException"/> на отрицательную численность.
    /// </summary>
    public EventLogEntry<GameSessionState> SetWorkerCount(Ulid teamId, Ulid factoryId, int count)
    {
        EnsureDecisionsAllowed();

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Worker count must not be negative.");
        }
        var team = GetTeam(teamId);
        GetFactory(team, factoryId);

        return _log.Append(new WorkerCountSet
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            FactoryId = factoryId,
            Count = count,
        });
    }

    /// <summary>Переключает фабрику на другой продукт (рецепт) из числа доступных её типу (Блок 9.1, SPEC §9.3). Требует фазы решений.</summary>
    public EventLogEntry<GameSessionState> SelectRecipe(Ulid teamId, Ulid factoryId, string recipeId)
    {
        EnsureDecisionsAllowed();

        var team = GetTeam(teamId);
        var factory = GetFactory(team, factoryId);
        var recipe = factory.Definition.Recipes.FirstOrDefault(r => r.Id == recipeId);
        if (recipe is null)
        {
            throw new ArgumentException(
                $"Recipe '{recipeId}' is not produced by factory definition '{factory.Definition.Id}'.", nameof(recipeId));
        }

        return _log.Append(new RecipeSelected
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            FactoryId = factoryId,
            RecipeId = recipe.Id,
        });
    }

    /// <summary>
    /// Меняет долю фабрики при разборе дефицитного сырья, общего с другими фабриками той же команды
    /// (см. doc-comment <see cref="Game.Domain.Factory.AllocationShare"/>). Требует фазы решений —
    /// та же логика, что и у остальных решений команды.
    /// </summary>
    public EventLogEntry<GameSessionState> SetFactoryAllocationShare(Ulid teamId, Ulid factoryId, decimal share)
    {
        EnsureDecisionsAllowed();

        var team = GetTeam(teamId);
        GetFactory(team, factoryId);

        return _log.Append(new FactoryAllocationShareSet
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            FactoryId = factoryId,
            Share = share,
        });
    }

    /// <summary>
    /// Меняет сумму, которую команда выделяет на R&amp;D конкретной фабрики за ход (SPEC §5.8) —
    /// само объявление бесплатно и мгновенно, как выбор рецепта или доля при дефиците сырья; реальное
    /// списание и рост уровня фабрики происходят отдельно, автоматически каждый ход (запрос
    /// пользователя: «постоянные затраты», не разовое вложение — см. <see cref="TickFinanceStep"/>).
    /// Требует фазы решений. Бросает <see cref="ArgumentOutOfRangeException"/> на отрицательную сумму
    /// или сумму сверх потолка <see cref="Config.Economy.RndConfig.MaxCommitmentPerTurn"/> (запрос
    /// пользователя: чтобы даже с любым кредитом нельзя было мгновенно прокачать фабрику на несколько
    /// уровней за один ход).
    /// </summary>
    public EventLogEntry<GameSessionState> SetRndCommitment(Ulid teamId, Ulid factoryId, decimal amountPerTurn)
    {
        EnsureDecisionsAllowed();

        var team = GetTeam(teamId);
        GetFactory(team, factoryId);

        var maxCommitmentPerTurn = State.Config.Raw.Rnd.MaxCommitmentPerTurn;
        if (amountPerTurn < 0 || amountPerTurn > maxCommitmentPerTurn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amountPerTurn), amountPerTurn, $"R&D commitment must be between 0 and {maxCommitmentPerTurn} per turn.");
        }

        return _log.Append(new RndCommitmentSet
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            FactoryId = factoryId,
            Amount = amountPerTurn,
        });
    }

    /// <summary>
    /// Меняет сумму, которую команда выделяет на исследование следующего поколения фабрик за ход
    /// (Блок 9.2, запрос пользователя: будущие фабрики должны появляться постепенно, через
    /// исследование) — то же самое декларативное действие, что и <see cref="SetRndCommitment"/>, но
    /// на уровне команды, а не одной фабрики; реальное списание и переход поколения происходят
    /// отдельно, автоматически каждый ход (см. <see cref="TickFinanceStep"/>). Требует фазы решений.
    /// Бросает <see cref="ArgumentOutOfRangeException"/> на отрицательную сумму или сумму сверх
    /// потолка <see cref="Config.Economy.GenerationResearchConfig.MaxCommitmentPerTurn"/>.
    /// </summary>
    public EventLogEntry<GameSessionState> SetGenerationResearchCommitment(Ulid teamId, decimal amountPerTurn)
    {
        EnsureDecisionsAllowed();

        GetTeam(teamId);

        var maxCommitmentPerTurn = State.Config.Raw.GenerationResearch.MaxCommitmentPerTurn;
        if (amountPerTurn < 0 || amountPerTurn > maxCommitmentPerTurn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amountPerTurn), amountPerTurn,
                $"Generation research commitment must be between 0 and {maxCommitmentPerTurn} per turn.");
        }

        return _log.Append(new GenerationResearchCommitmentSet
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            Amount = amountPerTurn,
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
    /// Ведущий выдаёт безвозмездный грант отстающей команде (Блок 9.6, SPEC §9.5). Не привязано к
    /// фазе решений — это действие ведущего, а не команды.
    /// </summary>
    public EventLogEntry<GameSessionState> GrantToTeam(Ulid teamId, decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Grant amount must be positive.");
        }
        GetTeam(teamId);

        return _log.Append(new GrantIssued { Id = Ulid.NewUlid(), TeamId = teamId, Amount = amount });
    }

    /// <summary>
    /// Ведущий включает/выключает аварийную закупку на время сессии (Блок 9.6, SPEC §9.5) — поверх
    /// стартового значения из конфига. Не привязано к фазе решений — это действие ведущего, а не команды.
    /// </summary>
    public EventLogEntry<GameSessionState> SetEmergencyPurchaseEnabled(bool enabled) =>
        _log.Append(new EmergencyPurchaseToggled { Id = Ulid.NewUlid(), Enabled = enabled });

    /// <summary>
    /// Ведущий вручную корректирует цену материала (Блок 9.6, SPEC §9.5), минуя обычный пересчёт
    /// рынка. Не привязано к фазе решений — это действие ведущего, а не команды.
    /// </summary>
    public EventLogEntry<GameSessionState> AdjustMarketPrice(string materialId, decimal newPrice)
    {
        if (newPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newPrice), newPrice, "Price must not be negative.");
        }
        GetQuoteOrThrow(materialId);

        return _log.Append(new MarketPriceAdjusted { Id = Ulid.NewUlid(), MaterialId = materialId, NewPrice = newPrice });
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
        ValidateParticipant(role, teamId, displayName);

        string code;
        do
        {
            code = ShortCode.Generate(codeRandom);
        }
        while (State.Participants.ContainsKey(code));

        return AppendParticipantRegistered(code, role, teamId, displayName);
    }

    /// <summary>
    /// Восстанавливает участника с уже выданным ему ранее кодом (Блок 10.2, SPEC §10: «те же...
    /// логины») — вместо генерации нового, как в <see cref="RegisterParticipant"/>. Нужно для
    /// сброса сессии (тренировка → основная игра): физически розданные бумажки/QR с кодами
    /// остаются рабочими и после сброса.
    /// </summary>
    public EventLogEntry<GameSessionState> ReregisterParticipant(
        string code, ParticipantRole role, Ulid? teamId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code must not be empty.", nameof(code));
        }
        ValidateParticipant(role, teamId, displayName);
        if (State.Participants.ContainsKey(code))
        {
            throw new ArgumentException($"Code '{code}' is already registered.", nameof(code));
        }

        return AppendParticipantRegistered(code, role, teamId, displayName);
    }

    /// <summary>Ищет зарегистрированного участника по коду входа; null, если код не зарегистрирован.</summary>
    public ParticipantRegistration? TryAuthenticate(string code) =>
        State.Participants.GetValueOrDefault(code);

    private void ValidateParticipant(ParticipantRole role, Ulid? teamId, string displayName)
    {
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
    }

    private EventLogEntry<GameSessionState> AppendParticipantRegistered(
        string code, ParticipantRole role, Ulid? teamId, string displayName) =>
        _log.Append(new ParticipantRegistered
        {
            Id = Ulid.NewUlid(),
            Code = code,
            Role = role,
            TeamId = teamId,
            DisplayName = displayName,
        });

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

    private NeedPosting GetNeed(Ulid needId)
    {
        if (!State.Needs.TryGetValue(needId, out var need))
        {
            throw new ArgumentException($"Unknown need posting '{needId}'.", nameof(needId));
        }

        return need;
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
    /// уровню материала, затем исполнение контрактов, затем для всех команд принудительный заём, если
    /// баланс всё ещё отрицательный (<see cref="ForcedLoanStep"/>), затем обновление рынка (Блок 6.1),
    /// затем новости по тренду (Блок 6.3) — оба публикуются даже без единой команды в сессии, они не
    /// зависят от них. Принудительный заём намеренно в самом конце, а не внутри финансового шага
    /// (баг-репорт пользователя: раньше решение принималось до переменных затрат на работу фабрики и
    /// исполнения контрактов — команда могла закрыть дыру займом и тут же снова уйти в минус от того,
    /// что на тот момент ещё не было посчитано, и это не покрывалось до следующего хода). События
    /// дописываются в журнал сразу по мере расчёта — не собираются заранее единым списком, — чтобы
    /// фабрика более высокого уровня видела в складе выход нижней в этом же тике, а последующая
    /// поставка — склад после предыдущей, и (для финансов) чтобы собственные срывы/расторжения этого
    /// же хода не успевали ударить по ставке, начисленной в его начале.
    /// <paramref name="newsRandom"/> — случайность подбора заголовка (AGENTS §2, правило 6:
    /// никакой случайности без явного, при необходимости засеянного, экземпляра); если пул
    /// заголовков текущего тренда в этой сессии исчерпан, новости в этот ход не будет. Вызывается
    /// автоматически (<see cref="PhaseAutoAdvancer"/>) сразу при входе в <see
    /// cref="TurnPhase.Settlement"/>, не дожидаясь истечения таймера фазы — сам расчёт мгновенный.
    /// </summary>
    public IReadOnlyList<EventLogEntry<GameSessionState>> RunTick(Random newsRandom)
    {
        if (State.CurrentPhase != TurnPhase.Settlement)
        {
            throw new InvalidOperationException(
                $"Cannot run a tick outside the '{TurnPhase.Settlement}' phase (currently '{State.CurrentPhase}').");
        }

        var appended = new List<EventLogEntry<GameSessionState>>();
        var config = State.Config;

        foreach (var team in State.Teams.Values.OrderBy(team => team.Id))
        {
            var reputation = GetReputation(team.Id);
            foreach (var change in TickFinanceStep.Run(
                team, config.Raw.StartingConditions, config.Raw.WorkerProductivity, config.Raw.Warehouse,
                config.Raw.FactoryDefinitions, config.Raw.Rnd, config.Raw.GenerationResearch, reputation.Percentage))
            {
                appended.Add(_log.Append(change));
            }

            // Уровни — строго по возрастанию, чтобы более высокий уровень видел в складе выход
            // более низкого за этот же тик (см. doc-comment выше). Внутри одного уровня фабрики
            // считаются одной группой (ProductionCalculator.CalculateGroup), а не по одной: если
            // несколько из них претендуют на один и тот же дефицитный материал, делят его по своей
            // AllocationShare, а не по тому, кого код обошёл первым.
            foreach (var levelGroup in team.Factories.GroupBy(f => f.SelectedRecipe.Output.Level).OrderBy(g => g.Key))
            {
                var factoriesAtLevel = levelGroup.OrderBy(f => f.Id).ToList();
                var results = ProductionCalculator.CalculateGroup(
                    factoriesAtLevel, team.Warehouse, config.Raw.WorkerProductivity, config.Raw.Rnd);

                foreach (var result in results)
                {
                    var factory = factoriesAtLevel.Single(f => f.Id == result.FactoryId);
                    // Переменная часть затрат на работу фабрики (энергия) — растёт вместе с объёмом
                    // выпуска, а не с числом рабочих или потреблённым сырьём (запрос пользователя),
                    // и известна только здесь, после расчёта производства (см. doc-comment
                    // TickFinanceStep — фиксированная часть, FactoryUpkeepPaid, списана раньше).
                    var overheadCost = result.OutputQuantity
                                        * config.Raw.Economy.ElectricityConsumptionPerOutputUnit
                                        * State.Market.ElectricityPrice;
                    appended.Add(_log.Append(new FactoryProduced
                    {
                        Id = Ulid.NewUlid(),
                        TeamId = team.Id,
                        FactoryId = result.FactoryId,
                        CapacityLimitedOutputQuantity = result.CapacityLimitedOutputQuantity,
                        OutputQuantity = result.OutputQuantity,
                        ConsumedInputs = result.ConsumedInputs,
                        LaborCost = factory.Workers * config.Raw.WorkerProductivity.SalaryPerWorkerPerTurn,
                        OverheadCost = overheadCost,
                    }));
                }
            }
        }

        ExecuteContracts(appended);

        // Самый последний шаг тика (см. doc-comment выше) — только теперь известны все возможные
        // причины отрицательного баланса: финансы, переменные затраты на производство и исполнение
        // контрактов (оплата покупки, штраф за срыв поставки — оба тоже могут увести в минус).
        foreach (var team in State.Teams.Values.OrderBy(team => team.Id))
        {
            var forcedLoan = ForcedLoanStep.Run(team, config.Raw.StartingConditions);
            if (forcedLoan is not null)
            {
                appended.Add(_log.Append(forcedLoan));
            }
        }

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
