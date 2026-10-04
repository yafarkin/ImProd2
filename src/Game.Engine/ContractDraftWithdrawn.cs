namespace Game.Engine;

/// <summary>Переговорщик забрал свой черновик сделки, пока управляющий его не разобрал (SPEC §3).</summary>
public sealed record ContractDraftWithdrawn : Change<GameSessionState>
{
    /// <summary>Забираемый черновик.</summary>
    public required Ulid DraftId { get; init; }

    public override void Apply(GameSessionState state) => state.ContractDrafts[DraftId].Withdraw();
}
