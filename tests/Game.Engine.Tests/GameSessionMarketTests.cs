namespace Game.Engine.Tests;

/// <summary>Продажа системе и аварийная закупка по живой рыночной цене (Блок 6.1, SPEC §5.3-5.4).</summary>
public class GameSessionMarketTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    [Fact]
    public void Market_Quotes_Are_Already_Available_In_The_Very_First_Decision_Phase()
    {
        var (session, _, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        Assert.True(session.State.Market.HasQuote(TestGameConfig.Ore.Id));
        Assert.Equal(10m, session.State.Market.QuoteOf(TestGameConfig.Ore.Id).Price);
        Assert.Equal(100m, session.State.Market.QuoteOf(TestGameConfig.Ore.Id).Capacity);
    }

    [Fact]
    public void SellToSystem_Within_Capacity_Credits_The_Team_At_The_Full_Quote()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var team = session.State.Teams[buyerId];
        team.Warehouse.Add(TestGameConfig.Ore, 20m);
        var balanceBefore = team.Balance;

        session.SellToSystem(buyerId, "ore", 20m);

        // Ore: цена 10, ёмкость 100, margin по умолчанию 1 -> 20 * 10 = 200.
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Equal(balanceBefore + 200m, team.Balance);
    }

    [Fact]
    public void SellToSystem_Beyond_Capacity_Discounts_The_Overflow_And_Accumulates_Across_Sales_In_The_Same_Turn()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var buyer = session.State.Teams[buyerId];
        var seller = session.State.Teams[sellerId];
        buyer.Warehouse.Add(TestGameConfig.Sheet, 5m);
        seller.Warehouse.Add(TestGameConfig.Sheet, 5m);

        // Sheet: ёмкость этого хода = 8. Первая продажа съедает всю ёмкость (5 из 8), вторая
        // команда продаёт ещё 5, из которых 3 всё ещё в пределах ёмкости, 2 — сверх.
        session.SellToSystem(buyerId, "sheet", 5m);
        var sellerBalanceBefore = seller.Balance;
        session.SellToSystem(sellerId, "sheet", 5m);

        // Sheet: цена 25, margin (уровень 1) = 1.2 -> unit price 30.
        // 3 * 30 + 2 * (30 * 0.5) = 90 + 30 = 120.
        Assert.Equal(sellerBalanceBefore + 120m, seller.Balance);
    }

    [Fact]
    public void SellToSystem_Capacity_Resets_On_The_Next_Turns_Market_Update()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var team = session.State.Teams[buyerId];
        team.Warehouse.Add(TestGameConfig.Sheet, 16m);

        session.SellToSystem(buyerId, "sheet", 8m); // выбирает всю ёмкость хода 1
        Assert.Equal(0m, session.State.Market.RemainingCapacityOf("sheet"));

        ToNextCalculation(session);
        session.RunTick(); // публикует котировки хода 2 и обнуляет счётчик проданного

        Assert.Equal(8m, session.State.Market.RemainingCapacityOf("sheet"));
    }

    [Fact]
    public void SellToSystem_Throws_When_The_Team_Does_Not_Have_Enough_Stock()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        Assert.Throws<InvalidOperationException>(() => session.SellToSystem(buyerId, "ore", 1m));
    }

    [Fact]
    public void SellToSystem_Throws_Outside_The_Decision_Phase()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();

        Assert.Throws<InvalidOperationException>(() => session.SellToSystem(buyerId, "ore", 1m));
    }

    [Fact]
    public void EmergencyPurchase_Follows_The_Live_Market_Price_After_A_Market_Update()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        ToNextCalculation(session);
        session.RunTick(); // republishes turn-2 quotes (no trend configured -> same as turn 1)
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision, turn 2
        var balanceBefore = session.State.Teams[buyerId].Balance;

        session.EmergencyPurchase(buyerId, "ore", volume: 5m);

        // Тот же результат, что и на ходу 1 (тренд в TestGameConfig не задан), но теперь явно через
        // MarketUpdated, а не напрямую из статичного конфига.
        Assert.Equal(balanceBefore - 100m, session.State.Teams[buyerId].Balance);
    }

    private static void ToNextCalculation(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Calculation))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }
}
