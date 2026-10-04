namespace Game.Engine;

/// <summary>Управляющий вернул черновик сделки переговорщику, не подав по нему заявку (SPEC §3).</summary>
public sealed record ContractDraftReturned : Change<GameSessionState>
{
    /// <summary>Возвращаемый черновик.</summary>
    public required Ulid DraftId { get; init; }

    /// <summary>Причина возврата словами управляющего — необязательна.</summary>
    public string? Reason { get; init; }

    public override void Apply(GameSessionState state) => state.ContractDrafts[DraftId].Return(Reason);
}
