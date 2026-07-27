namespace Game.Engine;

/// <summary>
/// Оператор отклонил контракт на этапе подтверждения (Блок 9.5, SPEC §9.4: «отклонение с
/// причиной»). Контракт никогда не становится действующим — не бьёт по репутации и не несёт
/// штрафа, в отличие от <see cref="ContractTerminated"/>.
/// </summary>
public sealed record ContractRejected : Change<GameSessionState>
{
    /// <summary>Идентификатор отклоняемого контракта.</summary>
    public required Ulid ContractId { get; init; }

    /// <summary>Причина отклонения.</summary>
    public required string Reason { get; init; }

    public override void Apply(GameSessionState state) => state.Contracts[ContractId].Reject(Reason);
}
