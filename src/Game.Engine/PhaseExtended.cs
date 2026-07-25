namespace Game.Engine;

/// <summary>Ведущий продлил текущую фазу хода (SPEC §4: «продлить» — событие в журнале).</summary>
public sealed record PhaseExtended : Change<GameSessionState>
{
    /// <summary>На сколько продлена текущая фаза.</summary>
    public required TimeSpan By { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.PhaseExtensionSeconds += By;
    }
}
