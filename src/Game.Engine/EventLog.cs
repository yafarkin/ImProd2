using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Engine;

/// <summary>
/// Append-only хеш-сцепленный журнал (AGENTS §2, правило 5): единственный способ изменить
/// <see cref="State"/> — <see cref="Append"/>, который сериализует событие, хеширует его вместе
/// с хешем предыдущей записи (SHA-256), применяет к состоянию и только потом записывает запись —
/// поэтому изменение, которое не удалось применить, никогда не попадёт в журнал, и нет пути,
/// меняющего состояние в обход журнала. Durable-хранение и восстановление воспроизведением —
/// Блок 3.2, не этот тип.
/// </summary>
public sealed class EventLog<TState>
{
    /// <summary>Значение «хеш предыдущей записи» для самой первой записи журнала — перед ней ничего нет.</summary>
    public static readonly string GenesisHash = new('0', 64);

    private readonly List<EventLogEntry<TState>> _entries = new();
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>Живое состояние, к которому этот журнал применяет события.</summary>
    public TState State { get; }

    /// <summary>Все записанные записи в порядке добавления.</summary>
    public IReadOnlyList<EventLogEntry<TState>> Entries => _entries;

    public EventLog(TState state, JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions();
    }

    /// <summary>Применяет <paramref name="change"/> к <see cref="State"/> и записывает его в журнал.</summary>
    public EventLogEntry<TState> Append(Change<TState> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var previousHash = _entries.Count == 0 ? GenesisHash : _entries[^1].Hash;
        var entry = new EventLogEntry<TState>
        {
            SequenceNumber = _entries.Count,
            Change = change,
            PreviousHash = previousHash,
            Hash = ComputeHash(change, previousHash, _serializerOptions),
        };

        // Применяем до записи: изменение, бросившее исключение, не должно попасть в журнал.
        change.Apply(State);
        _entries.Add(entry);

        return entry;
    }

    /// <summary>Перепроверяет все хеши по собственным записям этого журнала; подробности — в статической перегрузке.</summary>
    public bool VerifyIntegrity()
    {
        return VerifyIntegrity(_entries, _serializerOptions);
    }

    /// <summary>
    /// Пересчитывает хеш каждой записи по её сохранённым <see cref="EventLogEntry{TState}.Change"/> и
    /// <see cref="EventLogEntry{TState}.PreviousHash"/>, и проверяет, что цепочка ссылок на хеш
    /// предыдущей записи не разорвана. Возвращает false, если какая-то запись была подменена,
    /// отредактирована, переставлена или удалена после добавления — так детектируется подмена.
    /// </summary>
    public static bool VerifyIntegrity(
        IReadOnlyList<EventLogEntry<TState>> entries, JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var options = serializerOptions ?? new JsonSerializerOptions();

        var expectedPreviousHash = GenesisHash;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.SequenceNumber != i)
            {
                return false;
            }

            if (entry.PreviousHash != expectedPreviousHash)
            {
                return false;
            }

            if (entry.Hash != ComputeHash(entry.Change, entry.PreviousHash, options))
            {
                return false;
            }

            expectedPreviousHash = entry.Hash;
        }

        return true;
    }

    private static string ComputeHash(Change<TState> change, string previousHash, JsonSerializerOptions options)
    {
        var changeJson = JsonSerializer.Serialize(change, change.GetType(), options);
        var payload = previousHash + changeJson;
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(hashBytes);
    }
}
