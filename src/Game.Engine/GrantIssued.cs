namespace Game.Engine;

/// <summary>
/// Ведущий выдал безвозмездный грант отстающей команде (Блок 9.6, SPEC §9.5) — в отличие от
/// <see cref="LoanTaken"/>/<see cref="ForcedLoanTaken"/>, не увеличивает <c>Debt</c>.
/// </summary>
public sealed record GrantIssued : Change<GameSessionState>
{
    /// <summary>Команда, получившая грант.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Сумма гранта.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state) => state.Teams[TeamId].Credit(Amount);
}
