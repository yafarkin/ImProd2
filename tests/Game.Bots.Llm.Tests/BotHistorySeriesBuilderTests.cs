namespace Game.Bots.Llm.Tests;

/// <summary>Проверяет разреженную экономическую историю по ходам (риск №1 из TODO #20, запрос пользователя 2026-08-16), на реальной сессии, без обращения к LLM.</summary>
public sealed class BotHistorySeriesBuilderTests
{
    [Fact]
    public void Build_OnFirstTurn_HasOnlyTurnOneSample()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();

        var history = BotHistorySeriesBuilder.Build(session, teamId);

        Assert.Contains("sampled turns: 1", history);
        Assert.Contains("YOUR NET WORTH BY TURN", history);
        Assert.Contains("ALL TEAMS' NET WORTH BY TURN", history);
        Assert.Contains("Команда", history);
        Assert.Contains("YOUR FACTORY OUTPUT BY TURN", history);
        Assert.Contains("(no production yet)", history);
        Assert.Contains("YOUR WAREHOUSE STOCK BY TURN", history);
        Assert.Contains("(no stock history yet)", history);
    }

    [Fact]
    public void Build_AfterSeveralTurns_SamplesEveryIntervalPlusFirstAndCurrent()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var random = new Random(42);

        for (var i = 0; i < 6; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        var history = BotHistorySeriesBuilder.Build(session, teamId, sampleInterval: 5);

        // Ход сейчас 7: первый (1), каждый 5-й (5) и текущий (7) — без дублей.
        Assert.Contains("sampled turns: 1, 5, 7", history);
    }

    [Fact]
    public void Build_AfterProduction_ShowsFactoryOutputAndWarehouseStockSeries()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var random = new Random(7);

        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;
        session.SetWorkerCount(teamId, factoryId, 10);

        for (var i = 0; i < 5; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        var history = BotHistorySeriesBuilder.Build(session, teamId, sampleInterval: 5);

        Assert.Contains("- iron-mine:", history);
        Assert.Contains("- ore:", history);
        Assert.DoesNotContain("(no production yet)", history);
        Assert.DoesNotContain("(no stock history yet)", history);
    }

    [Fact]
    public void Build_UnknownTeam_Throws()
    {
        var (session, _) = TestSession.StartSingleTeamSession();

        Assert.Throws<ArgumentException>(() => BotHistorySeriesBuilder.Build(session, Ulid.NewUlid()));
    }

    [Fact]
    public void Build_NonPositiveInterval_Throws()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();

        Assert.Throws<ArgumentOutOfRangeException>(() => BotHistorySeriesBuilder.Build(session, teamId, sampleInterval: 0));
    }
}
