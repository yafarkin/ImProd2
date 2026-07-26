using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Управляющий команды дал финальное подтверждение сделки (SPEC §3, §6) — контракт переходит в
/// действующий статус. Роль подтверждающего проверяется до записи в журнал (в
/// <see cref="GameSession.ConfirmContract"/>), здесь фиксируется уже состоявшийся факт.
/// </summary>
public sealed record ContractConfirmed : Change<GameSessionState>
{
    /// <summary>Идентификатор подтверждаемого контракта.</summary>
    public required Ulid ContractId { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Contracts[ContractId].Confirm(TeamRole.Manager);
    }
}
