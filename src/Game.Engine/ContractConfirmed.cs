using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Управляющий команды дал финальное подтверждение сделки (SPEC §3, §6) — контракт переходит в
/// действующий статус. Роль и то, что подтверждает именно контрагент (а не команда-инициатор),
/// проверяются до записи в журнал (в <see cref="GameSession.ConfirmContract"/>); здесь фиксируется
/// уже состоявшийся факт, включая то, чья именно команда подтвердила (SPEC §9.3: полная
/// прослеживаемость решений по журналу).
/// </summary>
public sealed record ContractConfirmed : Change<GameSessionState>
{
    /// <summary>Идентификатор подтверждаемого контракта.</summary>
    public required Ulid ContractId { get; init; }

    /// <summary>Команда, чей управляющий подтвердил сделку.</summary>
    public required Ulid ConfirmingTeamId { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Contracts[ContractId].Confirm(TeamRole.Manager, ConfirmingTeamId, state.CurrentTurn);
    }
}
