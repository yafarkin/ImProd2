namespace Game.Domain;

/// <summary>
/// Заявка на сделку, поданная одной стороной и ожидающая встречной (SPEC §6: «обе команды вводят
/// условия независимо»). До 2026-09-09 такой сущности не было вовсе: экран сам фабриковал вторую
/// заявку от имени контрагента, и «независимый ввод» существовал только на бумаге
/// (<c>docs/TODO.md</c> №16). Хранит полные условия — они нужны движку для сверки, — но экран
/// контрагента их не показывает: узнать числа можно только у человека, и это единственное, что
/// возвращает переговоры в зал.
/// </summary>
public sealed class PendingContractProposal
{
    /// <summary>Уникальный идентификатор заявки.</summary>
    public Ulid Id { get; }

    /// <summary>Условия и стороны, как их подала одна из команд.</summary>
    public ContractProposal Proposal { get; }

    /// <summary>Ход, на котором заявка подана.</summary>
    public int SubmittedOnTurn { get; }

    /// <summary>Текущий статус заявки.</summary>
    public ContractProposalStatus Status { get; private set; } = ContractProposalStatus.Open;

    /// <summary>Команда, подавшая заявку.</summary>
    public Ulid SubmittedByTeamId => Proposal.SubmittedByTeamId;

    /// <summary>Вторая сторона сделки — та, от которой ждут встречную заявку.</summary>
    public Ulid CounterpartyTeamId =>
        Proposal.SubmittedByTeamId == Proposal.BuyerTeamId ? Proposal.SellerTeamId : Proposal.BuyerTeamId;

    public PendingContractProposal(Ulid id, ContractProposal proposal, int submittedOnTurn)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Proposal id must not be empty.", nameof(id));
        }
        ArgumentNullException.ThrowIfNull(proposal);
        if (submittedOnTurn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(submittedOnTurn), submittedOnTurn, "Turn must be positive.");
        }

        Id = id;
        Proposal = proposal;
        SubmittedOnTurn = submittedOnTurn;
    }

    /// <summary>Заявка сошлась со встречной; вызывается только при заключении контракта.</summary>
    public void MarkMatched() => ChangeStatus(ContractProposalStatus.Matched);

    /// <summary>Автор отзывает свою заявку.</summary>
    public void Withdraw() => ChangeStatus(ContractProposalStatus.Withdrawn);

    /// <summary>Контрагент отказывается вести сделку по этой заявке.</summary>
    public void Reject() => ChangeStatus(ContractProposalStatus.Rejected);

    private void ChangeStatus(ContractProposalStatus next)
    {
        if (Status != ContractProposalStatus.Open)
        {
            throw new InvalidOperationException($"Cannot change a contract proposal in status '{Status}'.");
        }

        Status = next;
    }
}
