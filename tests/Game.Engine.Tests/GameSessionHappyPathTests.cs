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
        // Небольшая стартовая сумма — здесь интересны именно постройка/наём/закупка/продажа.
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(startingCash: 2000m);
        var team = session.State.Teams[teamId];

        // Ход 1 начинается сразу в фазе расчёта (SessionStarted уже опубликовал котировки хода 1);
        // решения команд — только в фазе Decision.
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1

        // --- Шаг 1: строим рудник и объявляем желаемую численность рабочих ---
        var mineBuilt = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.SetWorkerCount(teamId, mineBuilt.FactoryId, count: 5);
        Assert.Equal(100m, mineBuilt.Cost); // TestGameConfig: BuildCost = 100
        Assert.Equal(2000m - 100m, team.Balance); // постройка сразу; наём объявлен, но пока бесплатен — спишется на расчёте (см. financeCost ниже)

        // --- Шаг 2: заявляем аварийную закупку руды про запас (SPEC §5.3) — решение — только заявка
        // (SPEC §4), реальная покупка (склад, деньги) произойдёт один раз, на расчёте хода 2 ---
        var balanceBeforePurchaseRequest = team.Balance;
        var purchaseRequest = (EmergencyPurchaseRequested)session.EmergencyPurchase(teamId, TestGameConfig.Ore.Id, volume: 10m).Change;
        Assert.Equal(10m, purchaseRequest.Volume);
        Assert.Equal(balanceBeforePurchaseRequest, team.Balance); // не изменился сразу
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Ore)); // склад тоже не тронут

        // --- Шаг 3: строим сталелитейный завод следующего уровня и тоже объявляем штат ---
        var millBuilt = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mill.Id).Change;
        session.SetWorkerCount(teamId, millBuilt.FactoryId, count: 5);
        var balanceAfterDecisionPhase = team.Balance; // 2000 - 100 (рудник) - 100 (завод) = 1800

        // --- Ход 2: расчёт тика — сначала финансовый шаг (наём по обеим фабрикам), потом
        // разрешается заявка на аварийную закупку (до расчёта производства, SPEC §4), потом рудник
        // добывает руду, а завод в том же тике перерабатывает и купленную, и добытую руду в лист ---
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var tick = session.RunTick(new Random(1));

        var purchased = (EmergencyPurchased)tick.Single(e => e.Change is EmergencyPurchased).Change;
        Assert.Equal(20m, purchased.UnitPrice); // базовая цена руды 10 x множитель аварийной закупки 2
        Assert.Equal(200m, purchased.TotalCost);

        var mined = (FactoryProduced)tick.Single(e => e.Change is FactoryProduced p && p.FactoryId == mineBuilt.FactoryId).Change;
        var milled = (FactoryProduced)tick.Single(e => e.Change is FactoryProduced p && p.FactoryId == millBuilt.FactoryId).Change;
        Assert.Equal(5m, mined.OutputQuantity); // 5 рабочих x ProductionRate 1
        Assert.Equal(5m, milled.OutputQuantity); // мощности хватило на все 15 руды (10 купленной + 5 добытой) / 2
        Assert.Equal(5m, team.Warehouse.QuantityOf(TestGameConfig.Ore)); // 15 было - 10 потрачено заводом
        Assert.Equal(5m, team.Warehouse.QuantityOf(TestGameConfig.Sheet));

        var financeCost = 10 /* рабочих */ * 50m /* наём, settled здесь */
            + 10 /* рабочих */ * 5m /* зарплаты */ + purchased.TotalCost /* аварийная закупка, тоже settled здесь */;
        Assert.Equal(balanceAfterDecisionPhase - financeCost, team.Balance);

        // --- Шаг 4: заявляем продажу готового листа системе — тоже только заявка ---
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 2
        var balanceBeforeSaleRequest = team.Balance;
        var saleRequest = (MaterialSaleRequested)session.SellToSystem(teamId, TestGameConfig.Sheet.Id, volume: 5m).Change;
        Assert.Equal(5m, saleRequest.Volume);
        Assert.Equal(balanceBeforeSaleRequest, team.Balance); // не изменился сразу
        Assert.Equal(5m, team.Warehouse.QuantityOf(TestGameConfig.Sheet)); // склад тоже не тронут

        // --- Ход 3: расчёт тика применяет продажу до расчёта производства (SPEC §4) — продать можно
        // только то, что уже было на складе, а не свежий выпуск этого же хода. Фабрики при этом не
        // останавливаются сами по себе: обе продолжают работать и с той же численностью намалывают
        // за ход 3 ровно столько же, сколько за ход 2, — проданные 5 листов освобождают место, но
        // склад не остаётся пустым ---
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        var tick2 = session.RunTick(new Random(1));

        var sale = (MaterialSoldToSystem)tick2.Single(e => e.Change is MaterialSoldToSystem).Change;
        Assert.Equal(30m, sale.UnitPrice); // базовая цена листа 25 x множитель маржи уровня 1 (1.2)
        Assert.Equal(150m, sale.TotalRevenue);

        var minedTurn3 = (FactoryProduced)tick2.Single(e => e.Change is FactoryProduced p && p.FactoryId == mineBuilt.FactoryId).Change;
        var milledTurn3 = (FactoryProduced)tick2.Single(e => e.Change is FactoryProduced p && p.FactoryId == millBuilt.FactoryId).Change;
        Assert.Equal(5m, minedTurn3.OutputQuantity);
        Assert.Equal(5m, milledTurn3.OutputQuantity); // старые 5 (ход 2) + новые 5 (ход 3) руды -> 5 листов
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Ore)); // вся руда ушла в переработку
        Assert.Equal(5m, team.Warehouse.QuantityOf(TestGameConfig.Sheet)); // старые 5 проданы, эти — уже новый выпуск этого хода

        // Ход 3 — те же зарплаты (наём уже никого не меняет — DesiredWorkers == Workers),
        // без капитальных затрат (FixedCostPerTurn=0 в TestGameConfig), плюс сама продажа.
        var turn3FinanceCost = 10 * 5m;
        Assert.Equal(balanceBeforeSaleRequest - turn3FinanceCost + sale.TotalRevenue, team.Balance);

        Assert.True(session.VerifyIntegrity());
    }
}
