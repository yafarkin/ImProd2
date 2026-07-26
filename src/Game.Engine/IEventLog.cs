namespace Game.Engine;

/// <summary>
/// Общий контракт «применить событие и хранить историю» — и для in-memory <see cref="EventLog{TState}"/>
/// (тесты, боты), и для durable-обёртки поверх него из Game.Persistence (Блок 8.1: живой веб-процесс).
/// <see cref="GameSession"/> работает с любой реализацией одинаково, не зная и не заботясь о том,
/// дублируется ли история на диск.
/// </summary>
public interface IEventLog<TState>
{
    /// <summary>Живое состояние, к которому применяются события.</summary>
    TState State { get; }

    /// <summary>Все записанные записи в порядке добавления.</summary>
    IReadOnlyList<EventLogEntry<TState>> Entries { get; }

    /// <summary>Применяет событие к <see cref="State"/> и дописывает его в историю.</summary>
    EventLogEntry<TState> Append(Change<TState> change);

    /// <summary>Проверяет целостность хеш-цепочки записей.</summary>
    bool VerifyIntegrity();
}
