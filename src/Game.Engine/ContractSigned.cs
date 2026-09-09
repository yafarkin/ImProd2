namespace Game.Engine;

/// <summary>
/// Заявки двух сторон совпали, код подтверждения выдан — контракт зафиксирован в журнале в статусе
/// «ждёт подтверждения» (SPEC §6). Отдельно от <see cref="ContractConfirmed"/>: сведение условий и
/// финальное подтверждение управляющим — два разных факта.
/// </summary>
public sealed record ContractSigned : Change<GameSessionState>
{
    /// <summary>Снимок условий согласованного контракта.</summary>
    public required ContractSpec Contract { get; init; }

    /// <summary>
    /// Две сошедшиеся заявки (<see cref="ContractProposalSubmitted"/>), из которых возник контракт —
    /// живой путь заключения сделки людьми. Пусто у ботов: они не ведут переговоров и подают обе
    /// половины разом через <c>GameSession.SubmitContractProposals</c>, минуя хранимые заявки.
    /// Совпадение заявок и есть подписание — отдельного события на это нет.
    /// </summary>
    public IReadOnlyList<Ulid> MatchedProposalIds { get; init; } = Array.Empty<Ulid>();

    public override void Apply(GameSessionState state)
    {
        state.AddContract(Contract.ToContract(state));
        foreach (var proposalId in MatchedProposalIds)
        {
            state.ContractProposals[proposalId].MarkMatched();
        }
    }
}
