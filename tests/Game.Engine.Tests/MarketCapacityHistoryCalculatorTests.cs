using Game.Domain;

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

    /// <summary>
    /// <see cref="MaterialSoldToSystem"/> собирается напрямую через <c>EventLog.Append</c> (как и в
    /// <c>TurnHistoryCalculatorTests</c>), а не через <see cref="GameSession.SellToSystem"/> — после
    /// переноса продажи на расчёт (SPEC §4, Блок 9.3) продажа и сбрасывающий график
    /// <see cref="MarketUpdated"/> того же хода попадают в журнал одним атомарным <see
    /// cref="GameSession.RunTick"/>; снаружи (в том числе с большого экрана) физически нельзя увидеть
    /// журнал «между» ними — только целиком до или целиком после тика. Через живую сессию такую
    /// последовательность («продажа, а график ещё не сброшен») больше не воспроизвести — виджет
    /// поэтому временно снят с большого экрана (см. <c>BigScreenDisplay.BuildMarketCapacityChart</c>).
    /// Сам калькулятор при этом остаётся верным для любой последовательности событий, которую ему
    /// дают, — эти тесты продолжают проверять именно его логику.
    /// </summary>
    [Fact]
    public void SummarizeCurrentTurn_Tracks_Remaining_Capacity_Percentage_After_A_Sale_Within_Capacity()
    {
        var (log, buyer, _) = TestGameConfig.StartSessionWithTwoTeams();
        log.Append(new EmergencyPurchased { Id = Ulid.NewUlid(), Turn = 1, TeamId = buyer.Id, MaterialId = "ore", Volume = 20m, UnitPrice = 10m, TotalCost = 200m }); // склад для последующей продажи
        log.Append(new MaterialSoldToSystem
        {
            Id = Ulid.NewUlid(), TeamId = buyer.Id, MaterialId = "ore", Volume = 20m,
            WithinCapacityVolume = 20m, OverflowVolume = 0m, UnitPrice = 10m, TotalRevenue = 200m,
        });

        // Ore: ёмкость хода — 100, продано 20 -> остаток 80%.
        var points = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(log.Entries, TestGameConfig.Resolved)[TestGameConfig.Ore.Id];
        Assert.Equal(2, points.Count);
        Assert.Equal((0, 100m), points[0]);
        Assert.Equal(80m, points[1].RemainingCapacityPercentage);
        Assert.True(points[1].ElapsedSeconds >= 0);
    }

    [Fact]
    public void SummarizeCurrentTurn_Never_Goes_Below_Zero_When_Sales_Exceed_Capacity()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        log.Append(new EmergencyPurchased { Id = Ulid.NewUlid(), Turn = 1, TeamId = buyer.Id, MaterialId = "ore", Volume = 80m, UnitPrice = 10m, TotalCost = 800m });
        log.Append(new EmergencyPurchased { Id = Ulid.NewUlid(), Turn = 1, TeamId = seller.Id, MaterialId = "ore", Volume = 80m, UnitPrice = 10m, TotalCost = 800m });
        log.Append(new MaterialSoldToSystem
        {
            Id = Ulid.NewUlid(), TeamId = buyer.Id, MaterialId = "ore", Volume = 80m,
            WithinCapacityVolume = 80m, OverflowVolume = 0m, UnitPrice = 10m, TotalRevenue = 800m,
        });
        log.Append(new MaterialSoldToSystem // пробивает ёмкость 100 (80 + 80 = 160)
        {
            Id = Ulid.NewUlid(), TeamId = seller.Id, MaterialId = "ore", Volume = 80m,
            WithinCapacityVolume = 20m, OverflowVolume = 60m, UnitPrice = 10m, TotalRevenue = 20m * 10m + 60m * 5m,
        });

        var points = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(log.Entries, TestGameConfig.Resolved)[TestGameConfig.Ore.Id];
        Assert.Equal(3, points.Count);
        Assert.Equal(20m, points[1].RemainingCapacityPercentage);
        Assert.Equal(0m, points[2].RemainingCapacityPercentage);
    }

    [Fact]
    public void SummarizeCurrentTurn_Resets_When_A_New_Turn_Publishes_Fresh_Quotes()
    {
        var (log, buyer, _) = TestGameConfig.StartSessionWithTwoTeams();
        log.Append(new EmergencyPurchased { Id = Ulid.NewUlid(), Turn = 1, TeamId = buyer.Id, MaterialId = "ore", Volume = 20m, UnitPrice = 10m, TotalCost = 200m });
        log.Append(new MaterialSoldToSystem
        {
            Id = Ulid.NewUlid(), TeamId = buyer.Id, MaterialId = "ore", Volume = 20m,
            WithinCapacityVolume = 20m, OverflowVolume = 0m, UnitPrice = 10m, TotalRevenue = 200m,
        });
        log.Append(new MarketUpdated { Id = Ulid.NewUlid(), Quotes = TestGameConfig.Resolved.Raw.Economy.BaseMarketPerMaterial.ToDictionary(m => m.MaterialId, m => new MaterialQuote(m.BasePrice, m.BaseCapacity)), ElectricityPrice = 1m });

        var points = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(log.Entries, TestGameConfig.Resolved)[TestGameConfig.Ore.Id];
        Assert.Equal((0, 100m), Assert.Single(points));
    }
}
