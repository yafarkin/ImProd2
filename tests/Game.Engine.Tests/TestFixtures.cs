namespace Game.Engine.Tests;

/// <summary>
/// Stand-in state and events for exercising <see cref="EventLog{TState}"/>. Block 3.1 is pure
/// event-sourcing infrastructure — no concrete game event exists yet — so these fixtures play that
/// role for tests only, the same way domain tests use small local fixture types.
/// </summary>
internal sealed class TestState
{
    public List<string> Log { get; } = new();

    public int Counter { get; set; }
}

internal sealed record AddLogEntryChange : Change<TestState>
{
    public required string Text { get; init; }

    public override void Apply(TestState state)
    {
        state.Log.Add(Text);
    }
}

internal sealed record IncrementCounterChange : Change<TestState>
{
    public required int Amount { get; init; }

    public override void Apply(TestState state)
    {
        if (Amount <= 0)
        {
            throw new InvalidOperationException("Amount must be positive.");
        }

        state.Counter += Amount;
    }
}
