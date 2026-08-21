using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Сквозные сценарии контрактов через <see cref="GameSession"/> (Блок 5.2).</summary>
public class GameSessionContractsTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    /// <summary>Крутит фазы вперёд, пока сессия не окажется в фазе расчёта уже следующего хода.</summary>
    private static void ToNextSettlement(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Settlement))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    private static Ulid SignAndConfirmSpot(GameSession session, Ulid buyerId, Ulid sellerId)
    {
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        var contractId = result.Contract!.Id;
        session.ConfirmContract(contractId, TeamRole.Manager, sellerId);
        return contractId;
    }

    [Fact]
    public void SubmitContractProposals_On_Conflict_Writes_Nothing_And_Reports_Mismatches()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var entriesBefore = session.Entries.Count;

        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, volume: 10m);
        var (_, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, volume: 12m);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));

        Assert.False(result.IsMatched);
        Assert.Contains(ContractMismatchReason.TermsDiffer, result.Mismatches);
        Assert.Equal(entriesBefore, session.Entries.Count); // конфликт не пишется в журнал
        Assert.Empty(session.State.Contracts);
    }

    [Fact]
    public void SubmitContractProposals_Throws_Outside_The_Decision_Phase()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);

        // сессия открывается в фазе расчёта — сделки заключать нельзя
        Assert.Throws<InvalidOperationException>(() => session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1)));
    }

    [Fact]
    public void ConfirmContract_By_A_Negotiator_Throws()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));

        Assert.Throws<InvalidOperationException>(() => session.ConfirmContract(result.Contract!.Id, TeamRole.Negotiator, sellerId));
    }

    [Fact]
    public void RunTick_Delivers_A_Confirmed_Spot_Contract_When_The_Seller_Has_The_Goods()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var contractId = SignAndConfirmSpot(session, buyerId, sellerId); // delivery turn 2
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);
        ToNextSettlement(session); // ход 2, фаза расчёта

        session.RunTick(new Random(1));

        Assert.Equal(10m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(0m, session.State.Teams[sellerId].Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(ContractStatus.Completed, session.State.Contracts[contractId].Status);
        Assert.True(session.VerifyIntegrity());
    }

    [Fact]
    public void RunTick_Records_A_Delivery_Miss_When_The_Seller_Cannot_Supply()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var contractId = SignAndConfirmSpot(session, buyerId, sellerId); // продавцу нечего поставить
        ToNextSettlement(session);

        var appended = session.RunTick(new Random(1));

        var changes = appended.Select(e => e.Change).ToList();
        var miss = Assert.IsType<DeliveryMissed>(changes.Single(c => c is DeliveryMissed));
        Assert.Equal(contractId, miss.ContractId);
        Assert.Equal(20m, miss.PenaltyAmount); // 10 * 20 * 0.1
        Assert.Equal(ContractStatus.Completed, session.State.Contracts[contractId].Status);
        Assert.DoesNotContain(changes, c => c is ContractDelivered);
    }

    [Fact]
    public void RunTick_Delivery_Miss_Is_All_Or_Nothing_Even_With_Partial_Stock()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var contractId = SignAndConfirmSpot(session, buyerId, sellerId); // объём 10
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 9m, 0m); // на 1 меньше нужного
        ToNextSettlement(session);

        var appended = session.RunTick(new Random(1));

        var miss = Assert.IsType<DeliveryMissed>(appended.Select(e => e.Change).Single(c => c is DeliveryMissed));
        Assert.Equal(10m, miss.ShortfallVolume); // сорван весь объём, а не только недостача в 1
        Assert.Equal(0m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet)); // покупатель не получил ничего
        Assert.Equal(9m, session.State.Teams[sellerId].Warehouse.QuantityOf(TestGameConfig.Sheet)); // продавец остался со своими 9
    }

    [Fact]
    public void TerminateContract_Mutually_Costs_Nothing()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var contractId = SignAndConfirmSpot(session, buyerId, sellerId);
        var buyerBalanceBefore = session.State.Teams[buyerId].Balance;

        session.TerminateContract(contractId, ContractTerminationReason.Mutual, terminatingTeamId: null);

        Assert.Equal(ContractStatus.Terminated, session.State.Contracts[contractId].Status);
        Assert.Equal(buyerBalanceBefore, session.State.Teams[buyerId].Balance);
    }

    [Fact]
    public void TerminateContract_Voluntarily_Charges_The_Configured_Fee_To_The_Initiator()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var contractId = SignAndConfirmSpot(session, buyerId, sellerId);
        var buyerBalanceBefore = session.State.Teams[buyerId].Balance;

        session.TerminateContract(contractId, ContractTerminationReason.Voluntary, terminatingTeamId: buyerId);

        // VoluntaryTerminationFee в TestGameConfig = 100
        Assert.Equal(buyerBalanceBefore - 100m, session.State.Teams[buyerId].Balance);
    }

    [Fact]
    public void EmergencyPurchase_Buys_At_System_Price_Times_The_Multiplier()
    {
        // Решение — только заявка (SPEC §4, §5.3); реальная покупка — на расчёте.
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        // ore: себестоимость 5, множитель 2 -> 10 за единицу; 5 единиц -> 50
        session.EmergencyPurchase(buyerId, "ore", volume: 5m);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        var purchased = Assert.IsType<EmergencyPurchased>(Assert.Single(appended, e => e.Change is EmergencyPurchased).Change);
        Assert.Equal(50m, purchased.TotalCost);
        Assert.Equal(5m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Ore));
    }

    [Fact]
    public void RunTick_Delivers_A_Recurring_Contract_Every_Turn_In_Range()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var terms = new ContractTerms(
            ContractType.Recurring, TestGameConfig.Sheet, 10m, 20m, 0.1m,
            effectiveTurn: 2, spotDeliveryTurn: null, recurringEndTurn: 3);
        var buyerProposal = new ContractProposal(buyerId, sellerId, buyerId, terms);
        var sellerProposal = new ContractProposal(buyerId, sellerId, sellerId, terms);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        session.ConfirmContract(result.Contract!.Id, TeamRole.Manager, sellerId);
        var contractId = result.Contract!.Id;

        // ход 2: продавец обеспечен -> поставка
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);
        ToNextSettlement(session);
        session.RunTick(new Random(1));
        Assert.Equal(10m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(ContractStatus.Active, session.State.Contracts[contractId].Status); // recurring продолжается

        // ход 3: снова обеспечен -> вторая поставка
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);
        ToNextSettlement(session);
        session.RunTick(new Random(1));
        Assert.Equal(20m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet));
    }

    /// <summary>
    /// Живой лог пользователя: recurring-контракт заключён на одном ходу с окном ровно в 1 ход, но
    /// контрагент подтвердил его через несколько ходов после того, как то самое окно уже прошло бы
    /// при старой семантике («ход вступления в силу» — то, что стороны заявляют заранее и что
    /// остаётся неизменным). Раньше контракт становился Active с уже просроченным окном и не
    /// доставлял ничего, никогда, без единого сигнала (ни поставки, ни срыва). Теперь окно
    /// пересчитывается от хода подтверждения — контракт с той же согласованной длительностью (1 ход)
    /// исполняется на ближайшем реально достижимом ходу.
    /// </summary>
    [Fact]
    public void RunTick_Delivers_A_Recurring_Contract_Even_When_Confirmation_Comes_Several_Turns_After_The_Originally_Proposed_Window()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session); // ход 1, решения
        var terms = new ContractTerms(
            ContractType.Recurring, TestGameConfig.Sheet, 10m, 20m, 0.1m,
            effectiveTurn: 2, spotDeliveryTurn: null, recurringEndTurn: 2); // окно в 1 ход, как в живом логе
        var buyerProposal = new ContractProposal(buyerId, sellerId, buyerId, terms);
        var sellerProposal = new ContractProposal(buyerId, sellerId, sellerId, terms);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        var contractId = result.Contract!.Id;

        // Контрагент тянет с подтверждением несколько ходов — старое окно [2, 2] давно прошло
        // к этому моменту, контракт всё ещё PendingConfirmation.
        for (var i = 0; i < 3; i++)
        {
            ToNextSettlement(session);
            session.RunTick(new Random(1));
            ToDecisionPhase(session);
        }
        Assert.Equal(ContractStatus.PendingConfirmation, session.State.Contracts[contractId].Status);
        var turnAtConfirmation = session.State.CurrentTurn;

        session.ConfirmContract(contractId, TeamRole.Manager, sellerId);

        Assert.Equal(ContractStatus.Active, session.State.Contracts[contractId].Status);
        var confirmedTerms = session.State.Contracts[contractId].Terms;
        Assert.Equal(turnAtConfirmation + 1, confirmedTerms.EffectiveTurn);
        Assert.Equal(turnAtConfirmation + 1, confirmedTerms.RecurringEndTurn); // та же длительность — 1 ход

        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);
        ToNextSettlement(session);
        session.RunTick(new Random(1));

        Assert.Equal(10m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(ContractStatus.Active, session.State.Contracts[contractId].Status); // recurring не завершается сам — просто больше не due за пределами окна
    }

    /// <summary>Бессрочный recurring (запрос пользователя: «может быть контракт до отмены?») доставляет каждый ход без ограничения сверху, пока одна из сторон его не расторгнет — после чего поставки прекращаются.</summary>
    [Fact]
    public void RunTick_Delivers_An_Indefinite_Recurring_Contract_Every_Turn_Until_Terminated()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var terms = new ContractTerms(
            ContractType.Recurring, TestGameConfig.Sheet, 10m, 20m, 0.1m,
            effectiveTurn: 1, spotDeliveryTurn: null, recurringEndTurn: null); // бессрочно
        var buyerProposal = new ContractProposal(buyerId, sellerId, buyerId, terms);
        var sellerProposal = new ContractProposal(buyerId, sellerId, sellerId, terms);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        var contractId = result.Contract!.Id;
        session.ConfirmContract(contractId, TeamRole.Manager, sellerId);
        Assert.Null(session.State.Contracts[contractId].Terms.RecurringEndTurn);

        // Три хода подряд поставка идёт как обычно — никакого предела не видно.
        for (var i = 0; i < 3; i++)
        {
            session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);
            ToNextSettlement(session);
            session.RunTick(new Random(1));
        }
        Assert.Equal(30m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(ContractStatus.Active, session.State.Contracts[contractId].Status);

        // Расторжение по согласию останавливает поставки немедленно — «до отмены» и означало именно это.
        ToDecisionPhase(session);
        session.TerminateContract(contractId, ContractTerminationReason.Mutual, terminatingTeamId: null);
        Assert.Equal(ContractStatus.Terminated, session.State.Contracts[contractId].Status);

        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);
        ToNextSettlement(session);
        session.RunTick(new Random(1));

        Assert.Equal(30m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet)); // без изменений — контракт расторгнут
    }
}
