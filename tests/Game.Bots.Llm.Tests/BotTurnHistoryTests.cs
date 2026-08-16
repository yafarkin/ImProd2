namespace Game.Bots.Llm.Tests;

public sealed class BotTurnHistoryTests
{
    [Fact]
    public void Render_NoEntries_SaysFirstTurn()
    {
        var history = new BotTurnHistory();

        Assert.Contains("this is your first turn", history.Render());
    }

    [Fact]
    public void Render_IncludesTurnSummaryAndAnnotation()
    {
        var history = new BotTurnHistory();
        history.Add(new BotTurnHistoryEntry(3, "buildFactory(iron-mine)", "ramping up ore early"));

        var rendered = history.Render();

        Assert.Contains("Turn 3: buildFactory(iron-mine) — ramping up ore early", rendered);
    }

    [Fact]
    public void Render_NoAnnotation_OmitsDash()
    {
        var history = new BotTurnHistory();
        history.Add(new BotTurnHistoryEntry(1, "nop", null));

        Assert.Equal("YOUR PAST DECISIONS (most recent 1)\n- Turn 1: nop", history.Render());
    }

    [Fact]
    public void Add_BeyondWindow_DropsOldestEntry()
    {
        var history = new BotTurnHistory(window: 2);
        history.Add(new BotTurnHistoryEntry(1, "a", null));
        history.Add(new BotTurnHistoryEntry(2, "b", null));
        history.Add(new BotTurnHistoryEntry(3, "c", null));

        Assert.Equal(2, history.Entries.Count);
        Assert.Equal(2, history.Entries[0].Turn);
        Assert.Equal(3, history.Entries[1].Turn);
    }
}
