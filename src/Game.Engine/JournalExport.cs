using System.Text.Json;

namespace Game.Engine;

/// <summary>Один экспортируемый факт журнала (Блок 10.1, SPEC §12) — короткое имя типа события вместо полного, для чтения без программиста.</summary>
public sealed record JournalExportEntry
{
    public required int SequenceNumber { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string ChangeType { get; init; }
    public required JsonElement Change { get; init; }
}

/// <summary>
/// Сырой журнал сессии в читаемый JSON (Блок 10.1, SPEC §12: «сырой event log в JSON... для
/// анализа без программиста»). Не предназначен для обратной загрузки — тип события несёт короткое
/// имя класса, а не полное <c>AssemblyQualifiedName</c>, как в durable-журнале на диске.
/// </summary>
public static class JournalExport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ToJson(IReadOnlyList<EventLogEntry<GameSessionState>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var items = entries.Select(entry => new JournalExportEntry
        {
            SequenceNumber = entry.SequenceNumber,
            Timestamp = entry.Timestamp,
            ChangeType = entry.Change.GetType().Name,
            Change = JsonSerializer.SerializeToElement(entry.Change, entry.Change.GetType(), Options),
        }).ToList();

        return JsonSerializer.Serialize(items, Options);
    }
}
