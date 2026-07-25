namespace Game.Engine;

/// <summary>
/// Base type for every event that can mutate <typeparamref name="TState"/> (AGENTS §2 rule 5:
/// all state changes go through the journal). Pure event-sourcing infrastructure — concrete game
/// events (production, contracts, finances, ...) will subclass this once those domains exist;
/// none exist yet, so no concrete <see cref="Change{TState}"/> lives in this project.
/// </summary>
public abstract record Change<TState>
{
    /// <summary>
    /// Identifies this event. Supplied by the caller rather than generated here, so replaying a
    /// stored journal reproduces the exact same ids instead of minting new ones.
    /// </summary>
    public required Ulid Id { get; init; }

    /// <summary>Mutates <paramref name="state"/> in place to reflect this event having happened.</summary>
    public abstract void Apply(TState state);
}
