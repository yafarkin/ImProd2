using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Пересмотр условий recurring-контракта через <see cref="GameSession"/> (Блок 9.3, SPEC §6).</summary>
public class GameSessionContractRevisionTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    private static (GameSession Session, Ulid BuyerId, Ulid SellerId, Ulid ContractId) StartWithActiveRecurringContract()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var terms = new ContractTerms(
            ContractType.Recurring, TestGameConfig.Sheet, 10m, 20m, 0.1m,
            effectiveTurn: 2, spotDeliveryTurn: null, recurringEndTurn: 5);
        var buyerProposal = new ContractProposal(buyerId, sellerId, buyerId, terms);
        var sellerProposal = new ContractProposal(buyerId, sellerId, sellerId, terms);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        session.ConfirmContract(result.Contract!.Id, TeamRole.Manager);

        return (session, buyerId, sellerId, result.Contract!.Id);
    }

    private static (GameSession Session, Ulid BuyerId, Ulid SellerId, Ulid ContractId) StartWithSpotContract()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        session.ConfirmContract(result.Contract!.Id, TeamRole.Manager);

        return (session, buyerId, sellerId, result.Contract!.Id);
    }

    [Fact]
    public void ProposeContractRevision_Is_Reported_As_Pending()
    {
        var (session, buyerId, _, contractId) = StartWithActiveRecurringContract();

        session.ProposeContractRevision(contractId, buyerId, volume: 15m, unitPrice: 25m, penaltyRate: 0.2m, recurringEndTurn: 8);

        var pending = session.GetPendingContractRevision(contractId);
        Assert.NotNull(pending);
        Assert.Equal(buyerId, pending!.ProposingTeamId);
        Assert.Equal(15m, pending.Volume);
    }

    [Fact]
    public void ProposeContractRevision_Throws_For_A_Spot_Contract()
    {
        var (session, buyerId, _, contractId) = StartWithSpotContract();

        Assert.Throws<InvalidOperationException>(
            () => session.ProposeContractRevision(contractId, buyerId, 15m, 25m, 0.2m, 8));
    }

    [Fact]
    public void ProposeContractRevision_Throws_When_The_Contract_Is_Not_Active()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var terms = new ContractTerms(
            ContractType.Recurring, TestGameConfig.Sheet, 10m, 20m, 0.1m,
            effectiveTurn: 2, spotDeliveryTurn: null, recurringEndTurn: 5);
        var result = session.SubmitContractProposals(
            new ContractProposal(buyerId, sellerId, buyerId, terms),
            new ContractProposal(buyerId, sellerId, sellerId, terms),
            new Random(1)); // ещё не подтверждён -> PendingConfirmation

        Assert.Throws<InvalidOperationException>(
            () => session.ProposeContractRevision(result.Contract!.Id, buyerId, 15m, 25m, 0.2m, 8));
    }

    [Fact]
    public void ProposeContractRevision_Throws_For_A_Non_Party_Team()
    {
        var (session, _, _, contractId) = StartWithActiveRecurringContract();

        Assert.Throws<ArgumentException>(
            () => session.ProposeContractRevision(contractId, Ulid.NewUlid(), 15m, 25m, 0.2m, 8));
    }

    [Fact]
    public void ProposeContractRevision_Throws_When_A_Revision_Is_Already_Pending()
    {
        var (session, buyerId, _, contractId) = StartWithActiveRecurringContract();
        session.ProposeContractRevision(contractId, buyerId, 15m, 25m, 0.2m, 8);

        Assert.Throws<InvalidOperationException>(
            () => session.ProposeContractRevision(contractId, buyerId, 16m, 26m, 0.2m, 8));
    }

    [Fact]
    public void ProposeContractRevision_Throws_Outside_The_Decision_Phase()
    {
        var (session, buyerId, _, contractId) = StartWithActiveRecurringContract();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Closing

        Assert.Throws<InvalidOperationException>(
            () => session.ProposeContractRevision(contractId, buyerId, 15m, 25m, 0.2m, 8));
    }

    [Fact]
    public void RespondToContractRevision_Accept_Terminates_The_Old_Contract_And_Creates_An_Active_Replacement()
    {
        var (session, buyerId, sellerId, contractId) = StartWithActiveRecurringContract();
        session.ProposeContractRevision(contractId, buyerId, volume: 15m, unitPrice: 25m, penaltyRate: 0.2m, recurringEndTurn: 8);
        var buyerBalanceBefore = session.State.Teams[buyerId].Balance;
        var sellerBalanceBefore = session.State.Teams[sellerId].Balance;

        session.RespondToContractRevision(contractId, TeamRole.Manager, accept: true, new Random(2));

        Assert.Equal(ContractStatus.Terminated, session.State.Contracts[contractId].Status);
        var replacement = session.State.Contracts.Values.Single(c => c.SupersedesContractId == contractId);
        Assert.Equal(ContractStatus.Active, replacement.Status);
        Assert.Equal(15m, replacement.Terms.Volume);
        Assert.Equal(25m, replacement.Terms.UnitPrice);
        Assert.Equal(0.2m, replacement.Terms.PenaltyRate);
        Assert.Equal(8, replacement.Terms.RecurringEndTurn);
        Assert.Equal(buyerBalanceBefore, session.State.Teams[buyerId].Balance); // без штрафа
        Assert.Equal(sellerBalanceBefore, session.State.Teams[sellerId].Balance);
        Assert.Null(session.GetPendingContractRevision(contractId));
        Assert.True(session.VerifyIntegrity());
    }

    [Fact]
    public void RespondToContractRevision_Reject_Leaves_The_Contract_Unchanged()
    {
        var (session, buyerId, sellerId, contractId) = StartWithActiveRecurringContract();
        session.ProposeContractRevision(contractId, buyerId, volume: 15m, unitPrice: 25m, penaltyRate: 0.2m, recurringEndTurn: 8);
        var buyerBalanceBefore = session.State.Teams[buyerId].Balance;
        var sellerBalanceBefore = session.State.Teams[sellerId].Balance;
        var contractsCountBefore = session.State.Contracts.Count;

        session.RespondToContractRevision(contractId, TeamRole.Manager, accept: false, new Random(2));

        Assert.Equal(ContractStatus.Active, session.State.Contracts[contractId].Status);
        Assert.Equal(10m, session.State.Contracts[contractId].Terms.Volume); // условия не изменились
        Assert.Equal(contractsCountBefore, session.State.Contracts.Count); // новый контракт не создан
        Assert.Equal(buyerBalanceBefore, session.State.Teams[buyerId].Balance);
        Assert.Equal(sellerBalanceBefore, session.State.Teams[sellerId].Balance);
        Assert.Null(session.GetPendingContractRevision(contractId));
    }

    [Fact]
    public void RespondToContractRevision_Throws_For_A_Negotiator()
    {
        var (session, buyerId, _, contractId) = StartWithActiveRecurringContract();
        session.ProposeContractRevision(contractId, buyerId, 15m, 25m, 0.2m, 8);

        Assert.Throws<InvalidOperationException>(
            () => session.RespondToContractRevision(contractId, TeamRole.Negotiator, accept: true, new Random(2)));
    }

    [Fact]
    public void RespondToContractRevision_Throws_When_There_Is_No_Pending_Proposal()
    {
        var (session, _, _, contractId) = StartWithActiveRecurringContract();

        Assert.Throws<InvalidOperationException>(
            () => session.RespondToContractRevision(contractId, TeamRole.Manager, accept: true, new Random(2)));
    }

    [Fact]
    public void RespondToContractRevision_Throws_Outside_The_Decision_Phase()
    {
        var (session, buyerId, _, contractId) = StartWithActiveRecurringContract();
        session.ProposeContractRevision(contractId, buyerId, 15m, 25m, 0.2m, 8);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Closing

        Assert.Throws<InvalidOperationException>(
            () => session.RespondToContractRevision(contractId, TeamRole.Manager, accept: true, new Random(2)));
    }
}
