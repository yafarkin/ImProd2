using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Подтверждение/отклонение сделки оператором (Блок 9.5, SPEC §6, §9.4).</summary>
public class GameSessionOperatorConfirmationTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    private static (GameSession Session, Ulid BuyerId, Ulid SellerId, Ulid ContractId) StartWithPendingContract()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));

        return (session, buyerId, sellerId, result.Contract!.Id);
    }

    [Fact]
    public void ConfirmContractByOperator_Transitions_To_Active()
    {
        var (session, _, _, contractId) = StartWithPendingContract();

        var entry = session.ConfirmContractByOperator(contractId);

        Assert.IsType<ContractConfirmedByOperator>(entry.Change);
        Assert.Equal(ContractStatus.Active, session.State.Contracts[contractId].Status);
    }

    [Fact]
    public void ConfirmContractByOperator_Throws_When_Not_PendingConfirmation()
    {
        var (session, _, _, contractId) = StartWithPendingContract();
        session.ConfirmContractByOperator(contractId);

        Assert.Throws<InvalidOperationException>(() => session.ConfirmContractByOperator(contractId));
    }

    [Fact]
    public void ConfirmContractByOperator_Throws_Outside_The_Decision_Phase()
    {
        var (session, _, _, contractId) = StartWithPendingContract();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement

        Assert.Throws<InvalidOperationException>(() => session.ConfirmContractByOperator(contractId));
    }

    [Fact]
    public void RejectContract_Transitions_To_Rejected_With_The_Given_Reason()
    {
        var (session, _, _, contractId) = StartWithPendingContract();

        session.RejectContract(contractId, "код не совпадает с бланком");

        var contract = session.State.Contracts[contractId];
        Assert.Equal(ContractStatus.Rejected, contract.Status);
        Assert.Equal("код не совпадает с бланком", contract.RejectionReason);
    }

    [Fact]
    public void RejectContract_Throws_For_An_Empty_Reason()
    {
        var (session, _, _, contractId) = StartWithPendingContract();

        Assert.Throws<ArgumentException>(() => session.RejectContract(contractId, "  "));
    }

    [Fact]
    public void RejectContract_Throws_When_Not_PendingConfirmation()
    {
        var (session, _, _, contractId) = StartWithPendingContract();
        session.ConfirmContractByOperator(contractId);

        Assert.Throws<InvalidOperationException>(() => session.RejectContract(contractId, "передумали"));
    }

    [Fact]
    public void RejectContract_Throws_Outside_The_Decision_Phase()
    {
        var (session, _, _, contractId) = StartWithPendingContract();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement

        Assert.Throws<InvalidOperationException>(() => session.RejectContract(contractId, "передумали"));
    }

    [Fact]
    public void RejectContract_Does_Not_Affect_Reputation()
    {
        var (session, buyerId, sellerId, contractId) = StartWithPendingContract();
        var buyerReputationBefore = session.GetReputation(buyerId);
        var sellerReputationBefore = session.GetReputation(sellerId);

        session.RejectContract(contractId, "передумали");

        var buyerReputationAfter = session.GetReputation(buyerId);
        var sellerReputationAfter = session.GetReputation(sellerId);
        Assert.Equal(buyerReputationBefore.Percentage, buyerReputationAfter.Percentage);
        Assert.Equal(buyerReputationBefore.SampleCount, buyerReputationAfter.SampleCount);
        Assert.Equal(sellerReputationBefore.Percentage, sellerReputationAfter.Percentage);
        Assert.Equal(sellerReputationBefore.SampleCount, sellerReputationAfter.SampleCount);
    }

    [Fact]
    public void FindContractByConfirmationCode_Finds_The_Matching_Contract()
    {
        var (session, _, _, contractId) = StartWithPendingContract();
        var code = session.State.Contracts[contractId].ConfirmationCode;

        var found = session.FindContractByConfirmationCode(code);

        Assert.NotNull(found);
        Assert.Equal(contractId, found!.Id);
    }

    [Fact]
    public void FindContractByConfirmationCode_Returns_Null_For_An_Unknown_Code()
    {
        var (session, _, _, _) = StartWithPendingContract();

        Assert.Null(session.FindContractByConfirmationCode("NOSUCH"));
    }
}
