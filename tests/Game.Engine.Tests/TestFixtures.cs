namespace Game.Engine.Tests;

/// <summary>
/// Условные состояние и события для проверки <see cref="EventLog{TState}"/>. Блок 3.1 — чистая
/// инфраструктура event sourcing, ни одного конкретного игрового события ещё нет, поэтому эти
/// фикстуры играют его роль только в тестах — как и локальные фикстуры в тестах домена.
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
