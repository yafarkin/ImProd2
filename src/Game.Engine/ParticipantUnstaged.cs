namespace Game.Engine;

/// <summary>Участник убран из черновика до старта сессии.</summary>
public sealed record ParticipantUnstaged : Change<DraftState>
{
    /// <summary>Идентификатор убираемой записи в черновике.</summary>
    public required Ulid ParticipantId { get; init; }

    public override void Apply(DraftState state)
    {
        state.RemoveParticipant(ParticipantId);
    }
}
