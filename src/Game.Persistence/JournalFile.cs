using System.Text.Json;
using Game.Engine;

namespace Game.Persistence;

/// <summary>
/// Чтение и добавление записей durable-журнала на диске: одна строка JSON (JSON Lines) на
/// событие, дописываемая по мере вызовов <see cref="Append{TState}"/>. Не проверяет целостность —
/// это задача <see cref="EventLog{TState}.VerifyIntegrity()"/>, вызываемой на прочитанных записях.
/// </summary>
internal static class JournalFile
{
    /// <summary>Читает все записи журнала по порядку; пустой список, если файла ещё нет.</summary>
    public static List<EventLogEntry<TState>> Read<TState>(string path, JsonSerializerOptions options)
    {
        if (!File.Exists(path))
        {
            return new List<EventLogEntry<TState>>();
        }

        var entries = new List<EventLogEntry<TState>>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var record = JsonSerializer.Deserialize<DurableEventRecord>(line, options)
                         ?? throw new InvalidOperationException($"Journal line at '{path}' deserialized to null: '{line}'.");

            var changeType = Type.GetType(record.ChangeType)
                              ?? throw new InvalidOperationException(
                                  $"Journal at '{path}' references unknown event type '{record.ChangeType}'.");

            var change = (Change<TState>?)JsonSerializer.Deserialize(record.ChangeJson, changeType, options)
                         ?? throw new InvalidOperationException(
                             $"Journal event at '{path}' deserialized to null: '{record.ChangeJson}'.");

            entries.Add(new EventLogEntry<TState>
            {
                SequenceNumber = record.SequenceNumber,
                Change = change,
                Timestamp = record.Timestamp,
                PreviousHash = record.PreviousHash,
                Hash = record.Hash,
            });
        }

        return entries;
    }

    /// <summary>Дописывает одну запись журнала в конец файла, создавая файл, если его ещё нет.</summary>
    public static void Append<TState>(string path, EventLogEntry<TState> entry, JsonSerializerOptions options)
    {
        var record = new DurableEventRecord
        {
            SequenceNumber = entry.SequenceNumber,
            ChangeType = entry.Change.GetType().AssemblyQualifiedName
                         ?? throw new InvalidOperationException(
                             $"Event type '{entry.Change.GetType()}' has no assembly-qualified name."),
            ChangeJson = JsonSerializer.Serialize(entry.Change, entry.Change.GetType(), options),
            Timestamp = entry.Timestamp,
            PreviousHash = entry.PreviousHash,
            Hash = entry.Hash,
        };

        File.AppendAllText(path, JsonSerializer.Serialize(record, options) + Environment.NewLine);
    }
}
