namespace Game.Engine;

/// <summary>
/// One recorded, hash-chained position in an <see cref="EventLog{TState}"/>. Deliberately a
/// record with public init accessors (not just constructed internally by the log) so tests — and,
/// later, the durable journal reader (Block 3.2) — can rebuild a sequence of entries from storage
/// and hand it to <see cref="EventLog{TState}.VerifyIntegrity"/> without going through <c>Append</c>.
/// </summary>
public sealed record EventLogEntry<TState>
{
    /// <summary>Zero-based position of this entry in the journal.</summary>
    public required int SequenceNumber { get; init; }

    /// <summary>The event recorded at this position.</summary>
    public required Change<TState> Change { get; init; }

    /// <summary>Hash of the previous entry (the genesis entry chains from <see cref="EventLog{TState}.GenesisHash"/>).</summary>
    public required string PreviousHash { get; init; }

    /// <summary>SHA-256 of (<see cref="PreviousHash"/> + serialized <see cref="Change"/>).</summary>
    public required string Hash { get; init; }
}
