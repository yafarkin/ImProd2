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
        history.Add(new BotTurnHistoryEntry(3, [new BotTurnActionRecord("buildFactory(iron-mine)", "ramping up ore early")]));

        var rendered = history.Render();

        Assert.Contains("Turn 3: buildFactory(iron-mine) — ramping up ore early", rendered);
    }

    [Fact]
    public void Render_NoAnnotation_OmitsDash()
    {
        var history = new BotTurnHistory();
        history.Add(new BotTurnHistoryEntry(1, [new BotTurnActionRecord("nop", null)]));

        Assert.Equal("YOUR PAST DECISIONS (most recent 1)\n- Turn 1: nop", history.Render());
    }

    [Fact]
    public void Render_MultipleActionsInOneTurn_JoinedBySemicolon()
    {
        // Запрос пользователя 2026-08-16: "важно уметь несколько команд за один ход".
        var history = new BotTurnHistory();
        history.Add(new BotTurnHistoryEntry(5,
        [
            new BotTurnActionRecord("takeLoan(1000)", "bootstrap capital"),
            new BotTurnActionRecord("buildFactory(iron-mine)", null),
            new BotTurnActionRecord("nop", null),
        ]));

        Assert.Equal(
            "YOUR PAST DECISIONS (most recent 1)\n- Turn 5: takeLoan(1000) — bootstrap capital; buildFactory(iron-mine); nop",
            history.Render());
    }

    [Fact]
    public void Add_BeyondWindow_DropsOldestEntry()
    {
        var history = new BotTurnHistory(window: 2);
        history.Add(new BotTurnHistoryEntry(1, [new BotTurnActionRecord("a", null)]));
        history.Add(new BotTurnHistoryEntry(2, [new BotTurnActionRecord("b", null)]));
        history.Add(new BotTurnHistoryEntry(3, [new BotTurnActionRecord("c", null)]));

        Assert.Equal(2, history.Entries.Count);
        Assert.Equal(2, history.Entries[0].Turn);
        Assert.Equal(3, history.Entries[1].Turn);
    }
}
