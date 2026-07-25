using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Engine;

/// <summary>
/// Append-only, hash-chained journal (AGENTS §2 rule 5): the only way to change <see cref="State"/>
/// is <see cref="Append"/>, which serializes the event, hashes it together with the previous
/// entry's hash (SHA-256), applies it to state, and only then records the entry — so a change that
/// fails to apply never enters the journal, and there is no path that mutates state without going
/// through it. Durable storage and replay-based recovery are Block 3.2, not this type.
/// </summary>
public sealed class EventLog<TState>
{
    /// <summary>Previous-hash value for the first entry in a journal — there is nothing before it.</summary>
    public static readonly string GenesisHash = new('0', 64);

    private readonly List<EventLogEntry<TState>> _entries = new();
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>The live state this journal has been applying events to.</summary>
    public TState State { get; }

    /// <summary>All recorded entries, in append order.</summary>
    public IReadOnlyList<EventLogEntry<TState>> Entries => _entries;

    public EventLog(TState state, JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions();
    }

    /// <summary>Applies <paramref name="change"/> to <see cref="State"/> and records it in the journal.</summary>
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

        // Apply before recording: a change that throws must never end up in the journal.
        change.Apply(State);
        _entries.Add(entry);

        return entry;
    }

    /// <summary>Re-checks every recorded hash against this log's own entries; see the static overload for details.</summary>
    public bool VerifyIntegrity()
    {
        return VerifyIntegrity(_entries, _serializerOptions);
    }

    /// <summary>
    /// Recomputes each entry's hash from its recorded <see cref="EventLogEntry{TState}.Change"/> and
    /// <see cref="EventLogEntry{TState}.PreviousHash"/>, and checks that the chain of previous-hash
    /// references is unbroken. Returns false if any entry was substituted, edited, reordered, or
    /// removed after being appended — this is how tampering is detected.
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
