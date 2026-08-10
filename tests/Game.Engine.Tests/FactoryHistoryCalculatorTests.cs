using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Историческая аналитика по фабрикам одной команды для графиков на /team (Блок 9.1) — реплей журнала, тот же приём проверки, что и у <see cref="TurnHistoryCalculatorTests"/>.</summary>
public class FactoryHistoryCalculatorTests
{
    private static (GameSession Session, Ulid TeamId, Ulid FactoryId) BuildAndStaffAMine(int workers = 5)
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.SetWorkerCount(teamId, built.FactoryId, workers);

        return (session, teamId, built.FactoryId);
    }

    [Fact]
    public void Summarize_Returns_Empty_Series_When_The_Team_Has_Not_Been_Registered_Yet()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        var history = FactoryHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved, Ulid.NewUlid());

        Assert.Empty(history.StockByMaterialId);
        Assert.Empty(history.OutputByFactoryId);
        Assert.Empty(history.ConsumedInputsByFactoryId);
        Assert.Empty(history.ProfitByLevel);
        Assert.Empty(history.NetWorthByTurn);
        Assert.Empty(history.ReputationByTurn);
    }

    [Fact]
    public void Summarize_Snapshots_Net_Worth_At_The_End_Of_Each_Completed_Turn()
    {
        // Ход 1: баланс — стартовый заём 100 000 минус постройка (100) = 99 900; наём 5 рабочих
        // (5*50=250) в ход 1 только объявлен (SetWorkerCount бесплатен и мгновенен), реально спишется
        // только на расчёте хода 2 (см. TickFinanceStep/WorkforceStep — тот же приём, что и R&D).
        // Долг — сам заём, 100 000 (ещё ничего не погашено); чистая стоимость — разница,
        // 99 900 - 100 000 = -100 (сырой баланс выглядел бы позитивным, пряча реальный отрицательный
        // результат первого хода за долгом).
        var (session, teamId, _) = BuildAndStaffAMine(workers: 5);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));

        var history = FactoryHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved, teamId);

        var turn1 = Assert.Single(history.NetWorthByTurn, point => point.Turn == 1);
        Assert.Equal(-100m, turn1.NetWorth);
    }

    [Fact]
    public void Summarize_Snapshots_Reputation_Reflecting_Events_Up_To_Each_Turn_Not_The_Whole_Log()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var terms = new ContractTerms(
            ContractType.Spot, TestGameConfig.Sheet, volume: 10m, unitPrice: 20m, penaltyRate: 0.1m,
            effectiveTurn: 1, spotDeliveryTurn: 5, recurringEndTurn: null);
        var contract = new Contract(Ulid.NewUlid(), buyer.Id, seller.Id, terms, "ABC123");
        var spec = ContractSpec.From(contract);
        log.Append(new ContractSigned { Id = Ulid.NewUlid(), Contract = spec });
        log.Append(new ContractConfirmed { Id = Ulid.NewUlid(), ContractId = spec.ContractId, ConfirmingTeamId = buyer.Id });

        // 8 переходов фаз с хода 1 (Расчёт) доводят до хода 5 (Расчёт) — та же арифметика фаз, что и в движке.
        for (var i = 0; i < 8; i++)
        {
            log.Append(new PhaseAdvanced { Id = Ulid.NewUlid(), Trigger = PhaseTransitionTrigger.Timer });
        }

        // Срыв после WarmupTurns (3) — реально штрафует репутацию продавца.
        log.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = spec.ContractId, Turn = 5, ShortfallVolume = 10m, PenaltyAmount = 20m });

        var history = FactoryHistoryCalculator.Summarize(log.Entries, TestGameConfig.Resolved, seller.Id);

        Assert.All(history.ReputationByTurn.Where(point => point.Turn < 5), point => Assert.Equal(100m, point.ReputationPercentage));
        var turn5 = Assert.Single(history.ReputationByTurn, point => point.Turn == 5);
        Assert.Equal(0m, turn5.ReputationPercentage);
    }

    [Fact]
    public void Summarize_Tracks_Actual_Output_Per_Turn_From_FactoryProduced_Events()
    {
        var (session, teamId, factoryId) = BuildAndStaffAMine();

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1)); // 5 рабочих -> 5 руды
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 2
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        session.RunTick(new Random(1)); // ещё 5 руды

        var history = FactoryHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved, teamId);

        Assert.Equal(new[] { (2, 5m), (3, 5m) }, history.OutputByFactoryId[factoryId]);
    }

    [Fact]
    public void Summarize_Tracks_Consumed_Inputs_Per_Turn_Alongside_Output_From_The_Same_FactoryProduced_Event()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1
        session.EmergencyPurchase(teamId, TestGameConfig.Ore.Id, 1000m); // руды с избытком, чтобы не ограничивать выпуск
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mill.Id).Change;
        session.SetWorkerCount(teamId, built.FactoryId, 5); // == BaseWorkerCount, отдача линейная 1:1

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1)); // 5 рабочих -> 5 листов, потребляя 2 руды на лист = 10 руды

        var history = FactoryHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved, teamId);

        Assert.Equal(new[] { (2, 5m) }, history.OutputByFactoryId[built.FactoryId]);
        var (turn, consumedInputs) = Assert.Single(history.ConsumedInputsByFactoryId[built.FactoryId]);
        Assert.Equal(2, turn);
        Assert.Equal(10m, consumedInputs[TestGameConfig.Ore.Id]);
    }

    [Fact]
    public void Summarize_Snapshots_Real_Warehouse_Stock_At_The_End_Of_Each_Completed_Turn()
    {
        var (session, teamId, _) = BuildAndStaffAMine();

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1)); // склад: 5 руды

        var history = FactoryHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved, teamId);

        // Ход 1 закончился раньше, чем фабрика хоть что-то произвела (постройка и объявление
        // численности — тоже ход 1, а сам наём и расчёт производства идут только в Settlement
        // следующего хода) — руда на склад ещё ни разу не поступала, поэтому в Warehouse.Stock
        // (список только когда-либо пополнявшихся материалов) на тот момент её вообще нет. Финальный
        // флаш — по состоянию сразу после RunTick хода 2.
        Assert.Equal(new[] { (2, 5m) }, history.StockByMaterialId[TestGameConfig.Ore.Id]);
    }

    [Fact]
    public void Summarize_Groups_Profitability_By_Pyramid_Level_Matching_A_Live_Calculation_At_The_Same_State()
    {
        var (session, teamId, _) = BuildAndStaffAMine();
        // SessionStarted уже публикует базовые рыночные котировки (Блок 6.1) — ценовой сигнал есть
        // с хода 1; но численность рабочих на ход 1 только объявлена (SetWorkerCount), реальный наём
        // settled лишь на расчёте хода 2 (см. WorkforceStep) — снимок хода 1 в сравнение не берём, он
        // предсказуемо нулевой (рабочих физически ещё нет). У рудника нет входов, поэтому его
        // гипотетический выпуск не зависит от остатков склада и одинаков на обоих полностью
        // укомплектованных ходах ниже (2 и 3).
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 2
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 3
        session.RunTick(new Random(1));

        var team = session.State.Teams[teamId];
        var found = FactoryProfitabilityCalculator.TryCalculate(
            team.Factories.Single(), team.Factories, team.Warehouse, session.State.Market,
            TestGameConfig.Resolved.Raw.WorkerProductivity, TestGameConfig.Resolved.Raw.Rnd,
            out var liveEstimate);
        Assert.True(found);

        var history = FactoryHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved, teamId);

        var level = TestGameConfig.Mine.Recipes[0].Output.Level;
        Assert.Equal(new[] { 1, 2, 3 }, history.ProfitByLevel[level].Select(point => point.Turn));
        var turn1 = Assert.Single(history.ProfitByLevel[level], point => point.Turn == 1);
        Assert.Equal(0m, turn1.Profit); // рабочих на этот момент ещё нет физически — см. комментарий выше
        Assert.All(
            history.ProfitByLevel[level].Where(point => point.Turn is 2 or 3),
            point => Assert.Equal(liveEstimate.Profit, point.Profit));
    }

    [Fact]
    public void Summarize_Only_Reports_The_Requested_Teams_Data()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision, ход 1
        var buyerFactory = (FactoryBuilt)session.BuildFactory(buyerId, TestGameConfig.Mine.Id).Change;
        session.SetWorkerCount(buyerId, buyerFactory.FactoryId, 5);
        var sellerFactory = (FactoryBuilt)session.BuildFactory(sellerId, TestGameConfig.Mine.Id).Change;
        session.SetWorkerCount(sellerId, sellerFactory.FactoryId, 3);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        session.RunTick(new Random(1));

        var buyerHistory = FactoryHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved, buyerId);

        var onlyFactory = Assert.Single(buyerHistory.OutputByFactoryId);
        Assert.Equal(buyerFactory.FactoryId, onlyFactory.Key);
        Assert.Equal(5m, onlyFactory.Value.Single().OutputQuantity);
    }
}
