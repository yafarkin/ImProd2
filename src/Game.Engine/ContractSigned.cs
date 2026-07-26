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

    public override void Apply(GameSessionState state)
    {
        state.AddContract(Contract.ToContract(state));
    }
}
