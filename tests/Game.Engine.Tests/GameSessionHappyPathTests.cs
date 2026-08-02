namespace Game.Engine.Tests;

/// <summary>
/// Один прогон «сверху вниз» для одной команды — не проверяет какой-то отдельный блок, а служит
/// читаемым смоук-тестом всего движка целиком: построить рудник, купить руды про запас, построить
/// сталелитейный завод, дождаться тика (руда добыта и переработана в лист), продать лист системе.
/// Удобно ставить точки останова по шагам и смотреть State/Entries в отладчике.
/// </summary>
public class GameSessionHappyPathTests
{
    [Fact]
    public void One_Team_Builds_Buys_Produces_And_Sells_Across_Two_Turns()
    {
        // Небольшой стартовый заём (не MaxStartingLoanAmount), чтобы проценты по кредиту не забивали
        // остальные числа — здесь интересны именно постройка/наём/закупка/продажа.
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(startingLoan: 2000m);
        var team = session.State.Teams[teamId];

        // Ход 1 начинается сразу в фазе расчёта (SessionStarted уже опубликовал котировки хода 1);
        // решения команд — только в фазе Decision.
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1

        // --- Шаг 1: строим рудник и нанимаем рабочих ---
        var mineBuilt = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.HireWorkers(teamId, mineBuilt.FactoryId, count: 5);
        Assert.Equal(100m, mineBuilt.Cost); // TestGameConfig: BuildCost = 100
        Assert.Equal(2000m - 100m - 5 * 50m, team.Balance); // постройка + наём (HireCostPerWorker = 50)

        // --- Шаг 2: закупаем руду про запас (аварийная закупка у системы, SPEC §5.3) ---
        var balanceBeforePurchase = team.Balance;
        var purchase = (EmergencyPurchased)session.EmergencyPurchase(teamId, TestGameConfig.Ore.Id, volume: 10m).Change;
        Assert.Equal(20m, purchase.UnitPrice); // базовая цена руды 10 x множитель аварийной закупки 2
        Assert.Equal(200m, purchase.TotalCost);
        Assert.Equal(balanceBeforePurchase - 200m, team.Balance);
        Assert.Equal(10m, team.Warehouse.QuantityOf(TestGameConfig.Ore));

        // --- Шаг 3: строим сталелитейный завод следующего уровня и тоже нанимаем рабочих ---
        var millBuilt = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mill.Id).Change;
        session.HireWorkers(teamId, millBuilt.FactoryId, count: 5);
        var balanceAfterDecisionPhase = team.Balance;

        // --- Ход 2: расчёт тика — рудник добывает руду, завод в том же тике перерабатывает её в лист ---
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var tick = session.RunTick(new Random(1));

        var mined = (FactoryProduced)tick.Single(e => e.Change is FactoryProduced p && p.FactoryId == mineBuilt.FactoryId).Change;
        var milled = (FactoryProduced)tick.Single(e => e.Change is FactoryProduced p && p.FactoryId == millBuilt.FactoryId).Change;
        Assert.Equal(5m, mined.OutputQuantity); // 5 рабочих x ProductionRate 1
        Assert.Equal(5m, milled.OutputQuantity); // мощности хватило на все 15 руды (10 купленной + 5 добытой) / 2
        Assert.Equal(5m, team.Warehouse.QuantityOf(TestGameConfig.Ore)); // 15 было - 10 потрачено заводом
        Assert.Equal(5m, team.Warehouse.QuantityOf(TestGameConfig.Sheet));

        var financeCost = 2000m * 0.05m + 10 /* рабочих */ * 5m; // проценты по займу + зарплаты
        Assert.Equal(balanceAfterDecisionPhase - financeCost, team.Balance);

        // --- Шаг 4: продаём готовый лист системе ---
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 2
        var balanceBeforeSale = team.Balance;
        var sale = (MaterialSoldToSystem)session.SellToSystem(teamId, TestGameConfig.Sheet.Id, volume: 5m).Change;

        Assert.Equal(30m, sale.UnitPrice); // базовая цена листа 25 x множитель маржи уровня 1 (1.2)
        Assert.Equal(150m, sale.TotalRevenue);
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(balanceBeforeSale + 150m, team.Balance);

        Assert.True(session.VerifyIntegrity());
    }
}
