using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Сквозные сценарии контрактов через <see cref="GameSession"/> (Блок 5.2).</summary>
public class GameSessionContractsTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    /// <summary>Крутит фазы вперёд, пока сессия не окажется в фазе расчёта уже следующего хода.</summary>
    private static void ToNextCalculation(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Calculation))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    private static Ulid SignAndConfirmSpot(GameSession session, Ulid buyerId, Ulid sellerId)
    {
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        var contractId = result.Contract!.Id;
        session.ConfirmContract(contractId, TeamRole.Manager);
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

        Assert.Throws<InvalidOperationException>(() => session.ConfirmContract(result.Contract!.Id, TeamRole.Negotiator));
    }

    [Fact]
    public void RunTick_Delivers_A_Confirmed_Spot_Contract_When_The_Seller_Has_The_Goods()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var contractId = SignAndConfirmSpot(session, buyerId, sellerId); // delivery turn 2
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m);
        ToNextCalculation(session); // ход 2, фаза расчёта

        session.RunTick();

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
        ToNextCalculation(session);

        var appended = session.RunTick();

        var changes = appended.Select(e => e.Change).ToList();
        var miss = Assert.IsType<DeliveryMissed>(changes.Single(c => c is DeliveryMissed));
        Assert.Equal(contractId, miss.ContractId);
        Assert.Equal(20m, miss.PenaltyAmount); // 10 * 20 * 0.1
        Assert.Equal(ContractStatus.Completed, session.State.Contracts[contractId].Status);
        Assert.DoesNotContain(changes, c => c is ContractDelivered);
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
        var (session, buyerId, _) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var balanceBefore = session.State.Teams[buyerId].Balance;

        // ore: системная цена 10, множитель 2 -> 20 за единицу; 5 единиц -> 100
        session.EmergencyPurchase(buyerId, "ore", volume: 5m);

        Assert.Equal(5m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Equal(balanceBefore - 100m, session.State.Teams[buyerId].Balance);
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
        session.ConfirmContract(result.Contract!.Id, TeamRole.Manager);
        var contractId = result.Contract!.Id;

        // ход 2: продавец обеспечен -> поставка
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m);
        ToNextCalculation(session);
        session.RunTick();
        Assert.Equal(10m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(ContractStatus.Active, session.State.Contracts[contractId].Status); // recurring продолжается

        // ход 3: снова обеспечен -> вторая поставка
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m);
        ToNextCalculation(session);
        session.RunTick();
        Assert.Equal(20m, session.State.Teams[buyerId].Warehouse.QuantityOf(TestGameConfig.Sheet));
    }
}
