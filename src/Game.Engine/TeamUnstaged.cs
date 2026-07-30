namespace Game.Engine;

/// <summary>
/// Команда убрана из черновика до старта сессии — заодно убирает застейдженного управляющего этой
/// команды, если он уже был назначен (см. <see cref="DraftState.RemoveTeam"/>).
/// </summary>
public sealed record TeamUnstaged : Change<DraftState>
{
    /// <summary>Идентификатор убираемой команды.</summary>
    public required Ulid TeamId { get; init; }

    public override void Apply(DraftState state)
    {
        state.RemoveTeam(TeamId);
    }
}
