namespace Game.Persistence;

/// <summary>
/// Одна строка durable-журнала (JSON Lines): то же самое, что <see cref="Game.Engine.EventLogEntry{TState}"/>,
/// но с событием, разложенным на CLR-тип (для полиморфной десериализации) и его сериализованное
/// содержимое, а не типизированный объект.
/// </summary>
internal sealed record DurableEventRecord
{
    /// <summary>Позиция записи в журнале, считая от нуля.</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>Полное имя CLR-типа события (сборка + тип), чтобы при чтении знать, во что десериализовать <see cref="ChangeJson"/>.</summary>
    public required string ChangeType { get; init; }

    /// <summary>Событие, сериализованное в JSON тем же способом, что участвовал в расчёте <see cref="Hash"/>.</summary>
    public required string ChangeJson { get; init; }

    /// <summary>Реальная метка времени добавления записи — см. <see cref="Game.Engine.EventLogEntry{TState}.Timestamp"/>.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Хеш предыдущей записи.</summary>
    public required string PreviousHash { get; init; }

    /// <summary>SHA-256 от (PreviousHash + Timestamp + ChangeJson) — как считает <see cref="Game.Engine.EventLog{TState}"/>.</summary>
    public required string Hash { get; init; }
}
