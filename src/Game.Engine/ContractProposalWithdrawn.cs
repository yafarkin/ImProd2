namespace Game.Engine;

/// <summary>Команда отозвала свою ещё не сведённую заявку на сделку (SPEC §6, <c>docs/TODO.md</c> №16).</summary>
public sealed record ContractProposalWithdrawn : Change<GameSessionState>
{
    /// <summary>Отзываемая заявка.</summary>
    public required Ulid ProposalId { get; init; }

    public override void Apply(GameSessionState state) => state.ContractProposals[ProposalId].Withdraw();
}
