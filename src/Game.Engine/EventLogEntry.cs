namespace Game.Engine;

/// <summary>
/// Одна записанная, хеш-сцепленная позиция в <see cref="EventLog{TState}"/>. Намеренно record с
/// публичными init-свойствами (а не только с внутренним конструированием журналом), чтобы тесты —
/// а позже и читатель durable-журнала (Блок 3.2) — могли собрать последовательность записей из
/// хранилища и передать её в <see cref="EventLog{TState}.VerifyIntegrity"/>, минуя <c>Append</c>.
/// </summary>
public sealed record EventLogEntry<TState>
{
    /// <summary>Позиция этой записи в журнале, считая от нуля.</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>Событие, записанное на этой позиции.</summary>
    public required Change<TState> Change { get; init; }

    /// <summary>Хеш предыдущей записи (первая запись сцепляется с <see cref="EventLog{TState}.GenesisHash"/>).</summary>
    public required string PreviousHash { get; init; }

    /// <summary>SHA-256 от (<see cref="PreviousHash"/> + сериализованное <see cref="Change"/>).</summary>
    public required string Hash { get; init; }
}
