using System.Text.Json.Nodes;

namespace Game.Persistence.Tests;

public class DurableEventLogTests
{
    private static (string JournalPath, string SnapshotPath) TempPaths()
    {
        var id = Guid.NewGuid().ToString("N");

        return (
            Path.Combine(Path.GetTempPath(), $"durable-eventlog-{id}.journal.jsonl"),
            Path.Combine(Path.GetTempPath(), $"durable-eventlog-{id}.snapshot.json"));
    }

    private static void CleanUp(string journalPath, string snapshotPath)
    {
        File.Delete(journalPath);
        File.Delete(snapshotPath);
        File.Delete(snapshotPath + ".tmp");
    }

    [Fact]
    public void Open_With_No_Existing_Files_Starts_From_A_Fresh_State()
    {
        var (journalPath, snapshotPath) = TempPaths();
        try
        {
            var log = DurableEventLog<TestState>.Open(journalPath, snapshotPath, () => new TestState());

            Assert.Empty(log.Entries);
            Assert.Empty(log.State.Log);
            Assert.Equal(0, log.State.Counter);
        }
        finally
        {
            CleanUp(journalPath, snapshotPath);
        }
    }

    [Fact]
    public void Append_Persists_Each_Entry_To_The_Journal_File_On_Disk()
    {
        var (journalPath, snapshotPath) = TempPaths();
        try
        {
            var log = DurableEventLog<TestState>.Open(journalPath, snapshotPath, () => new TestState());

            log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "hello" });
            log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "world" });

            Assert.True(File.Exists(journalPath));
            Assert.Equal(2, File.ReadAllLines(journalPath).Length);
        }
        finally
        {
            CleanUp(journalPath, snapshotPath);
        }
    }

    [Fact]
    public void N_Events_Then_Snapshot_Then_More_Events_Then_Restart_Restores_Identical_State()
    {
        var (journalPath, snapshotPath) = TempPaths();
        try
        {
            var log = DurableEventLog<TestState>.Open(journalPath, snapshotPath, () => new TestState());
            log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "a" });
            log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "b" });
            log.Append(new IncrementCounterChange { Id = Ulid.NewUlid(), Amount = 5 });

            log.Snapshot();

            log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "c" });
            log.Append(new IncrementCounterChange { Id = Ulid.NewUlid(), Amount = 2 });

            var expectedLog = log.State.Log.ToList();
            var expectedCounter = log.State.Counter;
            var expectedEntryCount = log.Entries.Count;
            var expectedLastHash = log.Entries[^1].Hash;

            // «Перезапуск»: открываем журнал заново по тем же путям, как это сделал бы новый процесс.
            var restarted = DurableEventLog<TestState>.Open(journalPath, snapshotPath, () => new TestState());

            Assert.Equal(expectedLog, restarted.State.Log);
            Assert.Equal(expectedCounter, restarted.State.Counter);
            Assert.Equal(expectedEntryCount, restarted.Entries.Count);
            Assert.Equal(expectedLastHash, restarted.Entries[^1].Hash);
            Assert.True(restarted.VerifyIntegrity());

            // Цепочка должна продолжаться корректно, а не начинаться заново с нуля.
            var nextEntry = restarted.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "d" });
            Assert.Equal(expectedEntryCount, nextEntry.SequenceNumber);
            Assert.Equal(expectedLastHash, nextEntry.PreviousHash);
        }
        finally
        {
            CleanUp(journalPath, snapshotPath);
        }
    }

    [Fact]
    public void Restart_Without_A_Snapshot_Replays_The_Entire_Journal_From_Scratch()
    {
        var (journalPath, snapshotPath) = TempPaths();
        try
        {
            var log = DurableEventLog<TestState>.Open(journalPath, snapshotPath, () => new TestState());
            log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "a" });
            log.Append(new IncrementCounterChange { Id = Ulid.NewUlid(), Amount = 3 });

            var restarted = DurableEventLog<TestState>.Open(journalPath, snapshotPath, () => new TestState());

            Assert.Equal(new[] { "a" }, restarted.State.Log);
            Assert.Equal(3, restarted.State.Counter);
            Assert.Equal(2, restarted.Entries.Count);
        }
        finally
        {
            CleanUp(journalPath, snapshotPath);
        }
    }

    [Fact]
    public void Open_Throws_When_The_Journal_File_Was_Tampered_With()
    {
        var (journalPath, snapshotPath) = TempPaths();
        try
        {
            var log = DurableEventLog<TestState>.Open(journalPath, snapshotPath, () => new TestState());
            log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "a" });
            log.Append(new AddLogEntryChange { Id = Ulid.NewUlid(), Text = "b" });

            // Правим содержимое первой строки журнала в обход API, не пересчитывая хеш —
            // симулируем ручную правку durable-файла.
            var lines = File.ReadAllLines(journalPath);
            var record = JsonNode.Parse(lines[0])!.AsObject();
            var changeJson = JsonNode.Parse(record["ChangeJson"]!.GetValue<string>())!.AsObject();
            changeJson["Text"] = "tampered";
            record["ChangeJson"] = changeJson.ToJsonString();
            lines[0] = record.ToJsonString();
            File.WriteAllLines(journalPath, lines);

            Assert.Throws<InvalidOperationException>(
                () => DurableEventLog<TestState>.Open(journalPath, snapshotPath, () => new TestState()));
        }
        finally
        {
            CleanUp(journalPath, snapshotPath);
        }
    }
}
