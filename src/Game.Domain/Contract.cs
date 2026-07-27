namespace Game.Domain;

/// <summary>
/// Сделка между двумя командами на уровне команды и типа продукции, не конкретной фабрики
/// (SPEC §6). Условия (<see cref="Terms"/>) неизменяемы после создания — меняется только
/// <see cref="Status"/>. В реальном потоке возникает через <see cref="ContractFormation.TryMatch"/>,
/// когда независимо поданные заявки обеих сторон совпали; конструктор публичный — как и у
/// остальных сущностей домена (<see cref="Team"/>, <see cref="Factory"/>), чтобы оставаться
/// тестируемым напрямую.
/// </summary>
public sealed class Contract
{
    /// <summary>Уникальный идентификатор контракта.</summary>
    public Ulid Id { get; }

    /// <summary>Команда-покупатель.</summary>
    public Ulid BuyerTeamId { get; }

    /// <summary>Команда-продавец.</summary>
    public Ulid SellerTeamId { get; }

    /// <summary>Условия сделки — неизменяемы на всём протяжении жизни контракта.</summary>
    public ContractTerms Terms { get; }

    /// <summary>
    /// Короткий код, который команды переносят на бумажный бланк вместе с условиями — оператор
    /// подтверждает по нему, не вводя условия вручную (SPEC §6, §9.4).
    /// </summary>
    public string ConfirmationCode { get; }

    /// <summary>Текущий статус контракта.</summary>
    public ContractStatus Status { get; private set; }

    /// <summary>Причина прекращения — заполняется только когда <see cref="Status"/> становится <see cref="ContractStatus.Terminated"/>.</summary>
    public ContractTerminationReason? TerminationReason { get; private set; }

    /// <summary>Причина отклонения оператором — заполняется только когда <see cref="Status"/> становится <see cref="ContractStatus.Rejected"/>.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>
    /// Контракт, взамен которого заведён этот (Блок 9.3, SPEC §6: пересмотр условий — новый
    /// контракт вместо расторгнутого) — <c>null</c> для контрактов, заключённых обычным путём.
    /// </summary>
    public Ulid? SupersedesContractId { get; }

    public Contract(
        Ulid id, Ulid buyerTeamId, Ulid sellerTeamId, ContractTerms terms, string confirmationCode,
        Ulid? supersedesContractId = null)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Contract id must not be empty.", nameof(id));
        }
        if (buyerTeamId == Ulid.Empty)
        {
            throw new ArgumentException("Buyer team id must not be empty.", nameof(buyerTeamId));
        }
        if (sellerTeamId == Ulid.Empty)
        {
            throw new ArgumentException("Seller team id must not be empty.", nameof(sellerTeamId));
        }
        if (buyerTeamId == sellerTeamId)
        {
            throw new ArgumentException("A team cannot contract with itself.", nameof(sellerTeamId));
        }
        ArgumentNullException.ThrowIfNull(terms);
        if (string.IsNullOrWhiteSpace(confirmationCode))
        {
            throw new ArgumentException("Confirmation code must not be empty.", nameof(confirmationCode));
        }

        Id = id;
        BuyerTeamId = buyerTeamId;
        SellerTeamId = sellerTeamId;
        Terms = terms;
        ConfirmationCode = confirmationCode;
        Status = ContractStatus.PendingConfirmation;
        SupersedesContractId = supersedesContractId;
    }

    /// <summary>
    /// Финальное подтверждение сделки — только управляющий (SPEC §3: «финальное подтверждение
    /// сделок» — право управляющего, не переговорщика).
    /// </summary>
    public void Confirm(TeamRole confirmingRole)
    {
        if (confirmingRole != TeamRole.Manager)
        {
            throw new InvalidOperationException("Only a team manager can give the final confirmation of a contract.");
        }
        if (Status != ContractStatus.PendingConfirmation)
        {
            throw new InvalidOperationException($"Cannot confirm a contract in status '{Status}'.");
        }

        Status = ContractStatus.Active;
    }

    /// <summary>
    /// Подтверждение оператором по коду (Блок 9.5, SPEC §6, §9.4) — второй, равноправный путь к
    /// тому же результату, что и <see cref="Confirm"/>: без роли, потому что это не командное
    /// действие, а действие оператора (сама возможность вызвать метод — и есть авторизация, как у
    /// действий ведущего).
    /// </summary>
    public void ConfirmByOperator()
    {
        if (Status != ContractStatus.PendingConfirmation)
        {
            throw new InvalidOperationException($"Cannot confirm a contract in status '{Status}'.");
        }

        Status = ContractStatus.Active;
    }

    /// <summary>
    /// Оператор отклоняет контракт на этапе подтверждения (Блок 9.5, SPEC §9.4: «отклонение с
    /// причиной») — контракт никогда не становится действующим, поэтому не несёт ни штрафа, ни
    /// удара по репутации.
    /// </summary>
    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason must not be empty.", nameof(reason));
        }
        if (Status != ContractStatus.PendingConfirmation)
        {
            throw new InvalidOperationException($"Cannot reject a contract in status '{Status}'.");
        }

        Status = ContractStatus.Rejected;
        RejectionReason = reason;
    }

    /// <summary>
    /// Прекращает действующий контракт целиком (SPEC §6: mutual — без штрафов; voluntary — дорого).
    /// Расчёт самого штрафа и удара по репутации — забота Блока 5.2, здесь фиксируется только факт
    /// и причина прекращения.
    /// </summary>
    public void Terminate(ContractTerminationReason reason)
    {
        if (Status != ContractStatus.Active)
        {
            throw new InvalidOperationException($"Cannot terminate a contract in status '{Status}'.");
        }

        Status = ContractStatus.Terminated;
        TerminationReason = reason;
    }

    /// <summary>
    /// Помечает разовый (spot) контракт завершившим свою единственную поставку — успешную или
    /// сорванную (SPEC §6). Больше исполнять нечего, но это не расторжение: штраф/репутация за
    /// само завершение не начисляются.
    /// </summary>
    public void Complete()
    {
        if (Status != ContractStatus.Active)
        {
            throw new InvalidOperationException($"Cannot complete a contract in status '{Status}'.");
        }
        if (Terms.Type != ContractType.Spot)
        {
            throw new InvalidOperationException("Only a spot contract completes after a single delivery.");
        }

        Status = ContractStatus.Completed;
    }
}
