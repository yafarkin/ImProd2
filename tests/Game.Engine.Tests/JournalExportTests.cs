using System.Text.Json;

namespace Game.Engine.Tests;

/// <summary>Читаемый JSON-экспорт сырого журнала для дебрифа (Блок 10.1, SPEC §12).</summary>
public class JournalExportTests
{
    [Fact]
    public void ToJson_Produces_One_Readable_Entry_Per_Journal_Record()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam(startingCash: 500m);

        var json = JournalExport.ToJson(session.Entries);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal(session.Entries.Count, root.GetArrayLength());

        var first = root[0];
        Assert.Equal(session.Entries[0].SequenceNumber, first.GetProperty("SequenceNumber").GetInt32());
        Assert.Equal("SessionStarted", first.GetProperty("ChangeType").GetString());
        Assert.Equal("test", first.GetProperty("Change").GetProperty("PresetId").GetString());
    }

    [Fact]
    public void ToJson_Produces_A_Valid_Empty_Array_For_No_Entries()
    {
        var json = JournalExport.ToJson(Array.Empty<EventLogEntry<GameSessionState>>());

        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal(0, root.GetArrayLength());
    }
}
