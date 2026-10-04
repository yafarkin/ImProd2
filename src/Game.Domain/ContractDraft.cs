namespace Game.Domain;

/// <summary>
/// Черновик сделки, который переговорщик подготовил в зале и передал своему управляющему (SPEC §3).
/// До появления этой сущности черновик жил только в браузере того, кто его заполнил, и до управляющего
/// не доходил вовсе. Сам черновик ни к чему не обязывает: на сверку с контрагентом уходит только
/// заявка, которую по нему подаст управляющий (<see cref="PendingContractProposal"/>).
/// </summary>
public sealed class ContractDraft
{
    /// <summary>Уникальный идентификатор черновика.</summary>
    public Ulid Id { get; }

    /// <summary>Условия и стороны, как их записал переговорщик; подающей стороной всегда указана его команда.</summary>
    public ContractProposal Proposal { get; }

    /// <summary>Код входа переговорщика, подготовившего черновик.</summary>
    public string PreparedByParticipantCode { get; }

    /// <summary>Ход, на котором черновик передан управляющему.</summary>
    public int PreparedOnTurn { get; }

    /// <summary>Текущий статус черновика.</summary>
    public ContractDraftStatus Status { get; private set; } = ContractDraftStatus.AwaitingManager;

    /// <summary>Заявка, которую управляющий подал по черновику; есть только в статусе <see cref="ContractDraftStatus.Submitted"/>.</summary>
    public Ulid? SubmittedProposalId { get; private set; }

    /// <summary>Причина возврата, если управляющий её назвал; только в статусе <see cref="ContractDraftStatus.Returned"/>.</summary>
    public string? ReturnReason { get; private set; }

    /// <summary>Команда переговорщика — та, от имени которой будет подана заявка.</summary>
    public Ulid TeamId => Proposal.SubmittedByTeamId;

    public ContractDraft(Ulid id, ContractProposal proposal, string preparedByParticipantCode, int preparedOnTurn)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Draft id must not be empty.", nameof(id));
        }
        ArgumentNullException.ThrowIfNull(proposal);
        if (string.IsNullOrWhiteSpace(preparedByParticipantCode))
        {
            throw new ArgumentException("Participant code must not be empty.", nameof(preparedByParticipantCode));
        }
        if (preparedOnTurn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preparedOnTurn), preparedOnTurn, "Turn must be positive.");
        }

        Id = id;
        Proposal = proposal;
        PreparedByParticipantCode = preparedByParticipantCode;
        PreparedOnTurn = preparedOnTurn;
    }

    /// <summary>Управляющий подал по черновику заявку <paramref name="proposalId"/>.</summary>
    public void MarkSubmitted(Ulid proposalId)
    {
        ChangeStatus(ContractDraftStatus.Submitted);
        SubmittedProposalId = proposalId;
    }

    /// <summary>Управляющий вернул черновик переговорщику.</summary>
    public void Return(string? reason)
    {
        ChangeStatus(ContractDraftStatus.Returned);
        ReturnReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>Переговорщик забрал черновик.</summary>
    public void Withdraw() => ChangeStatus(ContractDraftStatus.Withdrawn);

    private void ChangeStatus(ContractDraftStatus next)
    {
        if (Status != ContractDraftStatus.AwaitingManager)
        {
            throw new InvalidOperationException($"Cannot change a contract draft in status '{Status}'.");
        }

        Status = next;
    }
}
