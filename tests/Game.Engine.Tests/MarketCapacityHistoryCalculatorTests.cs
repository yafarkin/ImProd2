namespace Game.Engine.Tests;

/// <summary>Живой остаток дневной ёмкости сырья в рамках текущего хода, для графика на большом экране (Блок 9.1).</summary>
public class MarketCapacityHistoryCalculatorTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    private static void ToNextSettlement(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Settlement))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    [Fact]
    public void SummarizeCurrentTurn_Returns_Empty_Before_The_Session_Has_Started()
    {
        var series = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(
            Array.Empty<EventLogEntry<GameSessionState>>(), TestGameConfig.Resolved);

        Assert.Empty(series);
    }

    [Fact]
    public void SummarizeCurrentTurn_Seeds_Raw_Materials_At_Full_Capacity_When_The_Turn_Starts()
    {
        var (session, _, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var series = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(session.Entries, TestGameConfig.Resolved);

        var orePoints = Assert.Single(series[TestGameConfig.Ore.Id]);
        Assert.Equal((0, 100m), orePoints);
        // "sheet" — не сырьё (Level 1 в TestGameConfig), в график ёмкости сырья не попадает.
        Assert.False(series.ContainsKey(TestGameConfig.Sheet.Id));
    }

    [Fact]
    public void SummarizeCurrentTurn_Tracks_Remaining_Capacity_Percentage_After_A_Sale_Within_Capacity()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        session.EmergencyPurchase(buyerId, "ore", volume: 20m);

        session.SellToSystem(buyerId, "ore", 20m);

        // Ore: ёмкость хода — 100, продано 20 -> остаток 80%.
        var points = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(session.Entries, TestGameConfig.Resolved)[TestGameConfig.Ore.Id];
        Assert.Equal(2, points.Count);
        Assert.Equal((0, 100m), points[0]);
        Assert.Equal(80m, points[1].RemainingCapacityPercentage);
        Assert.True(points[1].ElapsedSeconds >= 0);
    }

    [Fact]
    public void SummarizeCurrentTurn_Never_Goes_Below_Zero_When_Sales_Exceed_Capacity()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        session.EmergencyPurchase(buyerId, "ore", volume: 80m);
        session.EmergencyPurchase(sellerId, "ore", volume: 80m);

        session.SellToSystem(buyerId, "ore", 80m);
        session.SellToSystem(sellerId, "ore", 80m); // пробивает ёмкость 100 (80 + 80 = 160)

        var points = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(session.Entries, TestGameConfig.Resolved)[TestGameConfig.Ore.Id];
        Assert.Equal(3, points.Count);
        Assert.Equal(20m, points[1].RemainingCapacityPercentage);
        Assert.Equal(0m, points[2].RemainingCapacityPercentage);
    }

    [Fact]
    public void SummarizeCurrentTurn_Resets_When_A_New_Turn_Publishes_Fresh_Quotes()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        session.EmergencyPurchase(buyerId, "ore", volume: 20m);
        session.SellToSystem(buyerId, "ore", 20m);

        ToNextSettlement(session);
        session.RunTick(new Random(1)); // публикует котировки хода 2 через MarketUpdated

        var points = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(session.Entries, TestGameConfig.Resolved)[TestGameConfig.Ore.Id];
        Assert.Equal((0, 100m), Assert.Single(points));
    }
}
