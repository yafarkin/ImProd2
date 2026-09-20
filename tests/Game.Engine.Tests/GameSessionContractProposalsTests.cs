using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Настоящий двухсторонний ввод условий сделки (SPEC §6, <c>docs/TODO.md</c> №16): каждая сторона
/// подаёт свою заявку отдельно, система сводит их сама. До 2026-09-09 вторую заявку фабриковал экран
/// от имени контрагента, и «независимый ввод» существовал только на бумаге.
/// </summary>
public class GameSessionContractProposalsTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    [Fact]
    public void Single_Proposal_Waits_For_A_Counter_Proposal_Without_Creating_A_Contract()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var result = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));

        Assert.False(result.IsMatched);
        Assert.False(result.HasCounterpartyProposal);
        Assert.Empty(session.State.Contracts);

        var stored = session.State.ContractProposals[result.ProposalId];
        Assert.Equal(ContractProposalStatus.Open, stored.Status);
        Assert.Equal(buyerId, stored.SubmittedByTeamId);
        Assert.Equal(sellerId, stored.CounterpartyTeamId);
    }

    [Fact]
    public void Two_Matching_Proposals_From_Both_Sides_Sign_The_Contract()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var first = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));
        var second = session.SubmitContractProposal(sellerProposal, TeamRole.Manager, new Random(1));

        Assert.True(second.IsMatched);
        var contract = Assert.Single(session.State.Contracts.Values);
        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);

        // Инициатор — тот, кто подал первым: финальное подтверждение даёт вторая сторона.
        Assert.Equal(buyerId, contract.ProposedByTeamId);

        // Обе заявки помечены сведёнными и больше не висят открытыми.
        Assert.Equal(ContractProposalStatus.Matched, session.State.ContractProposals[first.ProposalId].Status);
        Assert.Equal(ContractProposalStatus.Matched, session.State.ContractProposals[second.ProposalId].Status);
    }

    [Fact]
    public void Signed_Contract_Records_Both_Matched_Proposals_In_The_Journal()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var first = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));
        var second = session.SubmitContractProposal(sellerProposal, TeamRole.Manager, new Random(1));

        var signed = session.Entries.Select(e => e.Change).OfType<ContractSigned>().Single();
        Assert.Equal(new[] { first.ProposalId, second.ProposalId }, signed.MatchedProposalIds);
    }

    [Fact]
    public void Mismatched_Counter_Proposal_Names_The_Field_But_Still_Stores_The_Proposal()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, unitPrice: 20m);
        var (_, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, unitPrice: 25m);
        session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));
        var result = session.SubmitContractProposal(sellerProposal, TeamRole.Manager, new Random(1));

        Assert.False(result.IsMatched);
        Assert.True(result.HasCounterpartyProposal);
        Assert.Equal(new[] { ContractMismatchReason.UnitPriceDiffers }, result.Mismatches);
        Assert.Empty(session.State.Contracts);

        // Несовпавшая заявка не пропадает — вторая сторона может пересогласовать и подать заново.
        Assert.Equal(ContractProposalStatus.Open, session.State.ContractProposals[result.ProposalId].Status);
    }

    [Fact]
    public void A_Team_Cannot_Match_Its_Own_Proposal_With_Itself()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));
        var again = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));

        Assert.False(again.IsMatched);
        Assert.Empty(session.State.Contracts);
    }

    [Fact]
    public void Only_A_Manager_Can_Submit_A_Proposal()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);

        Assert.Throws<InvalidOperationException>(
            () => session.SubmitContractProposal(buyerProposal, TeamRole.Negotiator, new Random(1)));
        Assert.Empty(session.State.ContractProposals);
    }

    [Fact]
    public void Author_Can_Withdraw_Its_Own_Proposal_But_Counterparty_Cannot()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var submitted = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));

        Assert.Throws<ArgumentException>(() => session.WithdrawContractProposal(submitted.ProposalId, sellerId));

        session.WithdrawContractProposal(submitted.ProposalId, buyerId);
        Assert.Equal(ContractProposalStatus.Withdrawn, session.State.ContractProposals[submitted.ProposalId].Status);
    }

    [Fact]
    public void Counterparty_Can_Reject_A_Proposal_But_Author_Cannot()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var submitted = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));

        Assert.Throws<ArgumentException>(() => session.RejectContractProposal(submitted.ProposalId, buyerId));

        session.RejectContractProposal(submitted.ProposalId, sellerId);
        Assert.Equal(ContractProposalStatus.Rejected, session.State.ContractProposals[submitted.ProposalId].Status);
    }

    [Fact]
    public void A_Withdrawn_Proposal_No_Longer_Matches_A_Counter_Proposal()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var submitted = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));
        session.WithdrawContractProposal(submitted.ProposalId, buyerId);

        var result = session.SubmitContractProposal(sellerProposal, TeamRole.Manager, new Random(1));

        Assert.False(result.IsMatched);
        Assert.Empty(session.State.Contracts);
    }

    [Fact]
    public void Contract_Limit_Counts_Open_Proposals_Alongside_Live_Contracts()
    {
        // Лимит 1: одна открытая заявка уже исчерпывает квоту команды. Без учёта заявок лимит
        // обходился бы «настрогать офферов и подтверждать по мере надобности» (docs/TODO.md №16).
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams(
            config: TestGameConfig.BuildWithContractLimit(1));
        ToDecisionPhase(session);

        var (first, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.SubmitContractProposal(first, TeamRole.Manager, new Random(1));

        var (second, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, volume: 5m);
        Assert.Throws<InvalidOperationException>(
            () => session.SubmitContractProposal(second, TeamRole.Manager, new Random(1)));

        // Освободили квоту — снова можно подавать.
        var stored = Assert.Single(session.State.ContractProposals.Values);
        session.WithdrawContractProposal(stored.Id, buyerId);
        session.SubmitContractProposal(second, TeamRole.Manager, new Random(1));
        Assert.Equal(2, session.State.ContractProposals.Count);
    }

    [Fact]
    public void Proposals_Are_Rebuilt_By_Replaying_The_Journal()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);

        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        var submitted = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));
        session.RejectContractProposal(submitted.ProposalId, sellerId);

        var replayed = new GameSessionState(session.State.Config);
        foreach (var entry in session.Entries)
        {
            entry.Change.Apply(replayed);
        }

        var proposal = replayed.ContractProposals[submitted.ProposalId];
        Assert.Equal(ContractProposalStatus.Rejected, proposal.Status);
        Assert.Equal(buyerProposal.Terms.UnitPrice, proposal.Proposal.Terms.UnitPrice);
    }
}
