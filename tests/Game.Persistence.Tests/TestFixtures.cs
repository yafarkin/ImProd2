using Game.Engine;

namespace Game.Persistence.Tests;

/// <summary>
/// Условные состояние и события для проверки <see cref="DurableEventLog{TState}"/> — как и в
/// Game.Engine.Tests, ни одного реального игрового события ещё нет, поэтому тесты используют
/// такие же простые фикстуры (независимая копия — тестовые проекты не ссылаются друг на друга).
/// </summary>
internal sealed class TestState
{
    public List<string> Log { get; init; } = new();

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
        state.Counter += Amount;
    }
}
