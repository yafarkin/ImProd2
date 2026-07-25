using System.Text.Json;
using Game.Engine;

namespace Game.Persistence;

/// <summary>
/// Durable-обёртка над <see cref="EventLog{TState}"/> (Блок 3.1): каждый <see cref="Append"/>
/// дополнительно дописывает запись в журнал на диске; <see cref="Snapshot"/> сохраняет текущее
/// состояние, чтобы восстановление не доигрывало всю историю с начала. <see cref="Open{TState}"/> —
/// единственная точка входа как для новой сессии (файлов ещё нет), так и для восстановления после
/// сбоя (SPEC §11): снапшот + доигрывание хвоста журнала поверх него. Детерминизм (AGENTS §2,
/// правило 6) обеспечивается тем, что доигрывание — это те же самые события, применяемые в том же
/// порядке, что и исходно.
/// </summary>
public sealed class DurableEventLog<TState>
{
    private readonly EventLog<TState> _log;
    private readonly string _journalPath;
    private readonly string _snapshotPath;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>Живое состояние сессии.</summary>
    public TState State => _log.State;

    /// <summary>Вся история записей, включая восстановленные из журнала на диске.</summary>
    public IReadOnlyList<EventLogEntry<TState>> Entries => _log.Entries;

    private DurableEventLog(
        EventLog<TState> log, string journalPath, string snapshotPath, JsonSerializerOptions serializerOptions)
    {
        _log = log;
        _journalPath = journalPath;
        _snapshotPath = snapshotPath;
        _serializerOptions = serializerOptions;
    }

    /// <summary>
    /// Открывает сессию по путям журнала и снапшота: если файлов ещё нет — начинает с чистого
    /// состояния (<paramref name="createInitialState"/>); если есть — восстанавливает состояние из
    /// снапшота и доигрывает поверх него события журнала, случившиеся после снимка. Бросает, если
    /// журнал повреждён или подменён (см. <see cref="EventLog{TState}.VerifyIntegrity()"/>).
    /// </summary>
    public static DurableEventLog<TState> Open(
        string journalPath,
        string snapshotPath,
        Func<TState> createInitialState,
        JsonSerializerOptions? serializerOptions = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(createInitialState);
        var options = serializerOptions ?? new JsonSerializerOptions();

        var entries = JournalFile.Read<TState>(journalPath, options);
        var (state, lastSnapshotSequenceNumber) = SnapshotFile.Read(snapshotPath, createInitialState, options);

        foreach (var entry in entries.Where(entry => entry.SequenceNumber > lastSnapshotSequenceNumber))
        {
            entry.Change.Apply(state);
        }

        EventLog<TState> log;
        try
        {
            log = new EventLog<TState>(state, entries, options, clock);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Journal at '{journalPath}' failed integrity verification.", ex);
        }

        return new DurableEventLog<TState>(log, journalPath, snapshotPath, options);
    }

    /// <summary>Применяет событие и записывает его и в память, и в durable-журнал на диске.</summary>
    public EventLogEntry<TState> Append(Change<TState> change)
    {
        var entry = _log.Append(change);
        JournalFile.Append(_journalPath, entry, _serializerOptions);

        return entry;
    }

    /// <summary>Атомарно сохраняет текущее состояние как снапшот на последней применённой записи.</summary>
    public void Snapshot()
    {
        var lastSequenceNumber = _log.Entries.Count == 0 ? -1 : _log.Entries[^1].SequenceNumber;
        SnapshotFile.Write(_snapshotPath, State, lastSequenceNumber, _serializerOptions);
    }

    /// <summary>Проверяет целостность хеш-цепочки в памяти; подробности — в <see cref="EventLog{TState}.VerifyIntegrity()"/>.</summary>
    public bool VerifyIntegrity()
    {
        return _log.VerifyIntegrity();
    }
}
