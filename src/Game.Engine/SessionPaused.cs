namespace Game.Engine;

/// <summary>Ведущий поставил сессию на паузу (SPEC §4: «пауза» — событие в журнале).</summary>
public sealed record SessionPaused : Change<GameSessionState>
{
    public override void Apply(GameSessionState state)
    {
        state.IsPaused = true;
    }
}
