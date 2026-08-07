namespace Game.Engine.Tests;

/// <summary>
/// Продажа системе и аварийная закупка по живой рыночной цене (Блок 6.1, Блок 9.3, SPEC §5.3-5.4).
/// Решение (<see cref="GameSession.SellToSystem"/>/<see cref="GameSession.EmergencyPurchase"/>) —
/// только заявка (SPEC §4); реальное движение денег и склада — на расчёте (<see
/// cref="SystemSaleStep"/>/<see cref="EmergencyPurchaseStep"/>), покрыто отдельно в
/// <c>SystemSaleStepTests</c>/<c>EmergencyPurchaseStepTests</c>.
/// </summary>
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
    public void SellToSystem_Appends_A_MaterialSaleRequested_Event_Without_Touching_Stock_Or_Balance_Yet()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var team = session.State.Teams[buyerId];
        team.Warehouse.Add(TestGameConfig.Ore, 20m, 0m);
        var balanceBefore = team.Balance;

        var entry = session.SellToSystem(buyerId, "ore", 20m);

        var requested = Assert.IsType<MaterialSaleRequested>(entry.Change);
        Assert.Equal(20m, requested.Volume);
        Assert.Equal(20m, team.PendingSaleVolumeByMaterial["ore"]);
        Assert.Equal(20m, team.Warehouse.QuantityOf(TestGameConfig.Ore)); // склад не тронут
        Assert.Equal(balanceBefore, team.Balance); // баланс не тронут
    }

    [Fact]
    public void SellToSystem_Within_Capacity_Credits_The_Team_At_The_Full_Quote_After_The_Next_RunTick()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var team = session.State.Teams[buyerId];
        team.Warehouse.Add(TestGameConfig.Ore, 20m, 0m);
        session.SellToSystem(buyerId, "ore", 20m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        // Ore: цена 10, ёмкость 100, margin по умолчанию 1 -> 20 * 10 = 200.
        var sold = Assert.IsType<MaterialSoldToSystem>(Assert.Single(appended, e => e.Change is MaterialSoldToSystem).Change);
        Assert.Equal(200m, sold.TotalRevenue);
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Empty(team.PendingSaleVolumeByMaterial);
    }

    [Fact]
    public void SellToSystem_Beyond_Capacity_Discounts_The_Overflow_And_Accumulates_Across_Teams_In_The_Same_Turn()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var buyer = session.State.Teams[buyerId];
        var seller = session.State.Teams[sellerId];
        buyer.Warehouse.Add(TestGameConfig.Sheet, 5m, 0m);
        seller.Warehouse.Add(TestGameConfig.Sheet, 5m, 0m);

        // Sheet: ёмкость этого хода = 8. Обе заявки поданы за один и тот же ход решений — при расчёте
        // они разрешаются в детерминированном порядке команд (по возрастанию Team.Id, SPEC §4), а не
        // по тому, кто раньше нажал: первая по порядку съедает всю доступную ёмкость (5 из 8), вторая
        // продаёт ещё 5, из которых 3 всё ещё в пределах ёмкости, 2 — сверх, со скидкой.
        session.SellToSystem(buyerId, "sheet", 5m);
        session.SellToSystem(sellerId, "sheet", 5m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        // Sheet: цена 25, margin (уровень 1) = 1.2 -> unit price 30, скидка за превышение — 0.5 -> 15.
        // Кто из двух команд по порядку Id обработан первой (получает полную цену за все 5 — 150),
        // а кто второй (3 * 30 + 2 * 15 = 120) — не фиксируем; фиксируем то, что не зависит от
        // порядка: суммарная выручка обеих команд и то, что каждая получила ровно одно из двух значений.
        var sales = appended.Where(e => e.Change is MaterialSoldToSystem).Select(e => (MaterialSoldToSystem)e.Change).ToList();
        Assert.Equal(2, sales.Count);
        Assert.Equal(270m, sales.Sum(s => s.TotalRevenue)); // 5*30 + (3*30 + 2*15)
        Assert.Equal(new[] { 120m, 150m }, sales.Select(s => s.TotalRevenue).OrderBy(x => x));
    }

    [Fact]
    public void SellToSystem_Capacity_Resets_On_The_Next_Turns_Market_Update()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var team = session.State.Teams[buyerId];
        team.Warehouse.Add(TestGameConfig.Sheet, 16m, 0m);
        session.SellToSystem(buyerId, "sheet", 8m); // заявка на всю ёмкость хода 1

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1)); // выбирает ёмкость хода 1 и публикует котировки хода 2

        Assert.Equal(8m, session.State.Market.RemainingCapacityOf("sheet"));
    }

    [Fact]
    public void SellToSystem_Quietly_Caps_To_The_Actual_Stock_At_Settlement_Time_Instead_Of_Throwing()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var entry = session.SellToSystem(buyerId, "ore", 1m); // склада вообще нет

        Assert.IsType<MaterialSaleRequested>(entry.Change); // не бросает — проверка отложена на расчёт

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        var sold = Assert.IsType<MaterialSoldToSystem>(Assert.Single(appended, e => e.Change is MaterialSoldToSystem).Change);
        Assert.Equal(0m, sold.Volume); // урезано до реального остатка (0)
    }

    [Fact]
    public void SellToSystem_Throws_Outside_The_Decision_Phase()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();

        Assert.Throws<InvalidOperationException>(() => session.SellToSystem(buyerId, "ore", 1m));
    }

    [Fact]
    public void EmergencyPurchase_Appends_An_EmergencyPurchaseRequested_Event_Without_Touching_Stock_Or_Balance_Yet()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var team = session.State.Teams[buyerId];
        var balanceBefore = team.Balance;

        var entry = session.EmergencyPurchase(buyerId, "ore", volume: 5m);

        var requested = Assert.IsType<EmergencyPurchaseRequested>(entry.Change);
        Assert.Equal(5m, requested.Volume);
        Assert.Equal(5m, team.PendingEmergencyPurchaseVolumeByMaterial["ore"]);
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Equal(balanceBefore, team.Balance);
    }

    [Fact]
    public void EmergencyPurchase_Follows_The_Live_Market_Price_After_A_Market_Update()
    {
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        ToNextSettlement(session);
        session.RunTick(new Random(1)); // republishes turn-2 quotes (no trend configured -> same as turn 1)
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision, turn 2
        session.EmergencyPurchase(buyerId, "ore", volume: 5m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var appended = session.RunTick(new Random(1));

        // Тот же результат, что и на ходу 1 (тренд в TestGameConfig не задан), но теперь явно через
        // MarketUpdated, а не напрямую из статичного конфига.
        var purchased = Assert.IsType<EmergencyPurchased>(Assert.Single(appended, e => e.Change is EmergencyPurchased).Change);
        Assert.Equal(100m, purchased.TotalCost);
    }

    private static void ToNextSettlement(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Settlement))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    /// <summary>Сессия на конфиге с ненулевой надбавкой за «давление» недавних экстренных закупок — по образцу <see cref="TestGameConfig.StartGameSessionWithOneTeam"/>, но с настраиваемым конфигом.</summary>
    private static (GameSession Session, Ulid TeamId) StartWithEmergencyPurchasePressure(decimal pressureMultiplierPerUnit)
    {
        var config = TestGameConfig.BuildWithEmergencyPurchasePressure(pressureMultiplierPerUnit);
        var teamId = Ulid.NewUlid();
        var log = new EventLog<GameSessionState>(new GameSessionState(config));
        var session = GameSession.StartWithEndTurn(
            log, "test", endTurn: 999,
            new[] { new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id } });
        log.Append(new LoanTaken { Id = Ulid.NewUlid(), TeamId = teamId, Amount = 100_000m });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        return (session, teamId);
    }

    [Fact]
    public void EmergencyPurchase_Charges_The_Base_Multiplier_With_No_Prior_Purchases()
    {
        var (session, teamId) = StartWithEmergencyPurchasePressure(pressureMultiplierPerUnit: 1m);
        session.EmergencyPurchase(teamId, "ore", volume: 5m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        // TestGameConfig: ore BasePrice=10, EmergencyPurchaseBaseMultiplier=2 -> 20/ед., без давления.
        var purchased = Assert.IsType<EmergencyPurchased>(Assert.Single(appended, e => e.Change is EmergencyPurchased).Change);
        Assert.Equal(20m, purchased.UnitPrice);
        Assert.Equal(100m, purchased.TotalCost);
    }

    [Fact]
    public void EmergencyPurchase_Charges_A_Higher_Price_On_A_Later_Turn_After_A_Prior_Purchase()
    {
        // Штраф «давления» теперь считается на расчёте по фактической истории уже применённых
        // EmergencyPurchased — работает между ходами (как и было задумано), но не между несколькими
        // заявками внутри одного хода: те теперь просто одна декларация, см. doc-comment
        // EmergencyPurchaseRequested (упрощение, запрос пользователя).
        var (session, teamId) = StartWithEmergencyPurchasePressure(pressureMultiplierPerUnit: 1m);
        session.EmergencyPurchase(teamId, "ore", volume: 5m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1)); // множитель 2 (базовый) — теперь есть давление 5
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 2

        session.EmergencyPurchase(teamId, "ore", volume: 5m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var appended = session.RunTick(new Random(1));

        // Первая закупка применилась на ходу 2, вторая — на ходу 3: разница в один ход, давление уже
        // успело чуть затухнуть по полураспаду (HalfLifeTurns=3 в TestGameConfig), а не осталось
        // полными 5 — раньше (мгновенное применение) это был тот же ход, без затухания вообще.
        // Множитель = база(2) + давление(5 * 0.5^(1/3)) * 1.
        var decay = (decimal)Math.Pow(0.5, 1.0 / 3);
        var expectedUnitPrice = 10m * (2m + 5m * decay);
        var second = Assert.IsType<EmergencyPurchased>(Assert.Single(appended, e => e.Change is EmergencyPurchased).Change);
        Assert.Equal(expectedUnitPrice, second.UnitPrice);
    }

    [Fact]
    public void EmergencyPurchase_Requests_For_The_Same_Turn_Collapse_Into_One_Declared_Volume()
    {
        // Прямое следствие упрощения (SET-семантика, как и у заявки на заём): несколько заявок по
        // одному материалу за один ход больше не суммируются и не эскалируют цену друг для друга —
        // считается только последняя.
        var (session, teamId) = StartWithEmergencyPurchasePressure(pressureMultiplierPerUnit: 1m);
        session.EmergencyPurchase(teamId, "ore", volume: 5m);
        session.EmergencyPurchase(teamId, "ore", volume: 8m); // передумали — считается только это

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        var purchased = Assert.IsType<EmergencyPurchased>(Assert.Single(appended, e => e.Change is EmergencyPurchased).Change);
        Assert.Equal(8m, purchased.Volume);
        Assert.Equal(20m, purchased.UnitPrice); // без давления — это единственная примененная закупка
    }

    [Fact]
    public void EmergencyPurchase_Does_Not_Escalate_The_Price_Of_A_Different_Material()
    {
        var (session, teamId) = StartWithEmergencyPurchasePressure(pressureMultiplierPerUnit: 1m);
        session.EmergencyPurchase(teamId, "ore", volume: 5m);
        session.EmergencyPurchase(teamId, "sheet", volume: 1m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        // TestGameConfig: sheet BasePrice=25 * база(2) = 50, без давления от закупок руды.
        var sheetPurchase = Assert.IsType<EmergencyPurchased>(
            Assert.Single(appended, e => e.Change is EmergencyPurchased p && p.MaterialId == "sheet").Change);
        Assert.Equal(50m, sheetPurchase.UnitPrice);
    }

    [Fact]
    public void EmergencyPurchase_Zero_Cancels_A_Pending_Request()
    {
        var (session, teamId) = StartWithEmergencyPurchasePressure(pressureMultiplierPerUnit: 1m);
        session.EmergencyPurchase(teamId, "ore", volume: 5m);
        session.EmergencyPurchase(teamId, "ore", volume: 0m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        Assert.DoesNotContain(appended, e => e.Change is EmergencyPurchased);
    }
}
