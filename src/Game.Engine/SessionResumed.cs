namespace Game.Engine;

/// <summary>Ведущий снял сессию с паузы.</summary>
public sealed record SessionResumed : Change<GameSessionState>
{
    public override void Apply(GameSessionState state)
    {
        state.IsPaused = false;
    }
}
