namespace Game.Engine.Tests;

public class EventLogTests
{
    [Fact]
    public void Append_Applies_Change_To_State_And_Records_An_Entry()
    {
        var log = new EventLog<TestState>(new TestState());

        var entry = log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "hello" });

        Assert.Equal(new[] { "hello" }, log.State.Log);
        Assert.Equal(0, entry.SequenceNumber);
        Assert.Equal(EventLog<TestState>.GenesisHash, entry.PreviousHash);
        Assert.NotEqual(entry.PreviousHash, entry.Hash);
    }

    [Fact]
    public void Append_Preserves_Order_And_Chains_Hashes_Across_Multiple_Entries()
    {
        var log = new EventLog<TestState>(new TestState());

        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "first" });
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "second" });
        log.Append(new IncrementCounterChange { Id = Ulid.NewUlid(), Amount = 3 });

        Assert.Equal(new[] { "first", "second" }, log.State.Log);
        Assert.Equal(3, log.State.Counter);

        Assert.Equal(new[] { 0, 1, 2 }, log.Entries.Select(e => e.SequenceNumber));
        Assert.Equal(log.Entries[0].Hash, log.Entries[1].PreviousHash);
        Assert.Equal(log.Entries[1].Hash, log.Entries[2].PreviousHash);
    }

    [Fact]
    public void Append_Does_Not_Record_An_Entry_When_Apply_Throws()
    {
        var log = new EventLog<TestState>(new TestState());

        Assert.Throws<InvalidOperationException>(
            () => log.Append(new IncrementCounterChange { Id = Ulid.NewUlid(), Amount = -1 }));

        Assert.Empty(log.Entries);
        Assert.Equal(0, log.State.Counter);
    }

    [Fact]
    public void VerifyIntegrity_Returns_True_For_An_Untampered_Log()
    {
        var log = new EventLog<TestState>(new TestState());
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "first" });
        log.Append(new IncrementCounterChange { Id = Ulid.NewUlid(), Amount = 5 });

        Assert.True(log.VerifyIntegrity());
    }

    [Fact]
    public void VerifyIntegrity_Detects_A_Substituted_Entry()
    {
        var log = new EventLog<TestState>(new TestState());
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "first" });
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "second" });

        // Simulate someone editing the stored event content without recomputing its hash
        // (e.g. hand-editing the durable journal file from Block 3.2).
        var tampered = log.Entries.ToList();
        tampered[0] = tampered[0] with { Change = new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "tampered" } };

        Assert.False(EventLog<TestState>.VerifyIntegrity(tampered));
    }

    [Fact]
    public void VerifyIntegrity_Detects_A_Broken_Previous_Hash_Link()
    {
        var log = new EventLog<TestState>(new TestState());
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "first" });
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "second" });
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "third" });

        // Simulate a dropped record: the chain no longer lines up.
        var withGapInMiddle = new[] { log.Entries[0], log.Entries[2] };

        Assert.False(EventLog<TestState>.VerifyIntegrity(withGapInMiddle));
    }

    [Fact]
    public void VerifyIntegrity_Detects_A_Reordered_Log()
    {
        var log = new EventLog<TestState>(new TestState());
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "first" });
        log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "second" });

        var reordered = new[] { log.Entries[1], log.Entries[0] };

        Assert.False(EventLog<TestState>.VerifyIntegrity(reordered));
    }
}
