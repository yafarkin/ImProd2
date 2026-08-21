namespace Game.Engine;

/// <summary>Ведущий выдал безвозмездный грант отстающей команде (Блок 9.6, SPEC §9.5) — просто зачисляется на баланс.</summary>
public sealed record GrantIssued : Change<GameSessionState>
{
    /// <summary>Команда, получившая грант.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Сумма гранта.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        team.Credit(Amount);
    }
}
