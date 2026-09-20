namespace Game.Engine;

/// <summary>
/// Контрагент отказался вести сделку по поданной ему заявке (SPEC §6, <c>docs/TODO.md</c> №16). До
/// 2026-09-09 отказаться могла только команда-получатель уже готового контракта, и то лишь руками
/// оператора (<see cref="ContractRejected"/>) — своей кнопки «отклонить» у неё не было вовсе.
/// </summary>
public sealed record ContractProposalRejected : Change<GameSessionState>
{
    /// <summary>Отклоняемая заявка.</summary>
    public required Ulid ProposalId { get; init; }

    /// <summary>Команда, отклонившая заявку, — всегда контрагент, не автор.</summary>
    public required Ulid RejectedByTeamId { get; init; }

    public override void Apply(GameSessionState state) => state.ContractProposals[ProposalId].Reject();
}
