using System.Text.Json;

namespace Game.Persistence;

/// <summary>
/// Чтение и запись файла снапшота. Запись — через временный файл и атомарное переименование, чтобы
/// сбой посреди записи (например, отключение питания Raspberry Pi) не оставил битый снапшот —
/// либо остаётся старый файл целиком, либо новый целиком, промежуточного состояния на диске нет.
/// </summary>
internal static class SnapshotFile
{
    /// <summary>Читает снапшот, если он есть; иначе возвращает начальное состояние от <paramref name="createInitialState"/> и -1.</summary>
    public static (TState State, int LastSequenceNumber) Read<TState>(
        string path, Func<TState> createInitialState, JsonSerializerOptions options)
    {
        if (!File.Exists(path))
        {
            return (createInitialState(), -1);
        }

        var json = File.ReadAllText(path);
        var record = JsonSerializer.Deserialize<SnapshotRecord<TState>>(json, options)
                     ?? throw new InvalidOperationException($"Snapshot at '{path}' deserialized to null.");

        return (record.State, record.SequenceNumber);
    }

    /// <summary>Атомарно записывает снапшот, перезаписывая предыдущий, если он был.</summary>
    public static void Write<TState>(string path, TState state, int lastSequenceNumber, JsonSerializerOptions options)
    {
        var record = new SnapshotRecord<TState> { SequenceNumber = lastSequenceNumber, State = state };
        var tempPath = path + ".tmp";

        File.WriteAllText(tempPath, JsonSerializer.Serialize(record, options));
        File.Move(tempPath, path, overwrite: true);
    }
}
