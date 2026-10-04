using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Черновик сделки от переговорщика к управляющему (SPEC §3). До этих событий черновик жил только в
/// браузере переговорщика и до управляющего не доходил вовсе (<c>docs/manager-ui/README.md</c> §2 п.20).
/// </summary>
public class GameSessionContractDraftsTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    private static string Register(GameSession session, ParticipantRole role, Ulid teamId, int seed) =>
        ((ParticipantRegistered)session.RegisterParticipant(role, teamId, $"{role} {seed}", new Random(seed)).Change).Code;

    private static (GameSession Session, Ulid BuyerId, Ulid SellerId, string NegotiatorCode) StartWithBuyerNegotiator()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        var negotiatorCode = Register(session, ParticipantRole.Negotiator, buyerId, seed: 1);
        ToDecisionPhase(session);
        return (session, buyerId, sellerId, negotiatorCode);
    }

    private static ContractDraft OnlyDraft(GameSession session) => Assert.Single(session.State.ContractDrafts.Values);

    [Fact]
    public void Negotiator_Hands_A_Draft_To_The_Manager_Without_Creating_A_Proposal()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);

        session.PrepareContractDraft(buyerProposal, negotiatorCode);

        var draft = OnlyDraft(session);
        Assert.Equal(ContractDraftStatus.AwaitingManager, draft.Status);
        Assert.Equal(buyerId, draft.TeamId);
        Assert.Equal(negotiatorCode, draft.PreparedByParticipantCode);
        Assert.Equal(session.State.CurrentTurn, draft.PreparedOnTurn);
        Assert.Equal(buyerProposal.Terms.UnitPrice, draft.Proposal.Terms.UnitPrice);

        // Черновик — внутреннее дело команды: контрагенту ничего не подано.
        Assert.Empty(session.State.ContractProposals);
    }

    [Fact]
    public void A_Manager_Cannot_Prepare_A_Draft()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        var managerCode = Register(session, ParticipantRole.Manager, buyerId, seed: 2);
        ToDecisionPhase(session);
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);

        Assert.Throws<InvalidOperationException>(() => session.PrepareContractDraft(buyerProposal, managerCode));
    }

    [Fact]
    public void A_Negotiator_Cannot_Prepare_A_Draft_On_Behalf_Of_Another_Team()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (_, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);

        Assert.Throws<ArgumentException>(() => session.PrepareContractDraft(sellerProposal, negotiatorCode));
    }

    [Fact]
    public void An_Unknown_Participant_Cannot_Prepare_A_Draft()
    {
        var (session, buyerId, sellerId, _) = StartWithBuyerNegotiator();
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);

        Assert.Throws<ArgumentException>(() => session.PrepareContractDraft(buyerProposal, "NOSUCH"));
    }

    [Fact]
    public void Drafts_Are_Only_Accepted_In_The_Decision_Phase()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        var negotiatorCode = Register(session, ParticipantRole.Negotiator, buyerId, seed: 1);
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);

        Assert.Throws<InvalidOperationException>(() => session.PrepareContractDraft(buyerProposal, negotiatorCode));
    }

    [Fact]
    public void Manager_Submits_A_Proposal_From_The_Draft_And_The_Draft_Closes_As_Submitted()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        var draftId = OnlyDraft(session).Id;

        var result = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1), draftId);

        var draft = session.State.ContractDrafts[draftId];
        Assert.Equal(ContractDraftStatus.Submitted, draft.Status);
        Assert.Equal(result.ProposalId, draft.SubmittedProposalId);
        var submitted = session.Entries.Select(e => e.Change).OfType<ContractProposalSubmitted>().Single();
        Assert.Equal(draftId, submitted.SourceDraftId);
    }

    [Fact]
    public void Manager_May_Correct_The_Terms_Before_Submitting_From_A_Draft()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (drafted, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, unitPrice: 20m);
        var (corrected, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, unitPrice: 18m);
        session.PrepareContractDraft(drafted, negotiatorCode);
        var draftId = OnlyDraft(session).Id;

        var result = session.SubmitContractProposal(corrected, TeamRole.Manager, new Random(1), draftId);

        Assert.Equal(18m, session.State.ContractProposals[result.ProposalId].Proposal.Terms.UnitPrice);
        Assert.Equal(20m, session.State.ContractDrafts[draftId].Proposal.Terms.UnitPrice);
    }

    [Fact]
    public void A_Draft_Submitted_Against_A_Matching_Counter_Proposal_Signs_The_Contract()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.SubmitContractProposal(sellerProposal, TeamRole.Manager, new Random(1));
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        var draftId = OnlyDraft(session).Id;

        var result = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1), draftId);

        Assert.True(result.IsMatched);
        Assert.Single(session.State.Contracts);
        Assert.Equal(ContractDraftStatus.Submitted, session.State.ContractDrafts[draftId].Status);
    }

    [Fact]
    public void A_Draft_Cannot_Be_Submitted_By_Another_Team()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        var draftId = OnlyDraft(session).Id;

        Assert.Throws<ArgumentException>(
            () => session.SubmitContractProposal(sellerProposal, TeamRole.Manager, new Random(1), draftId));
        Assert.Equal(ContractDraftStatus.AwaitingManager, session.State.ContractDrafts[draftId].Status);
        Assert.Empty(session.State.ContractProposals);
    }

    [Fact]
    public void Manager_Returns_A_Draft_With_A_Reason_And_It_Can_No_Longer_Be_Submitted()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        var draftId = OnlyDraft(session).Id;

        session.ReturnContractDraft(draftId, buyerId, "  дорого  ");

        var draft = session.State.ContractDrafts[draftId];
        Assert.Equal(ContractDraftStatus.Returned, draft.Status);
        Assert.Equal("дорого", draft.ReturnReason);
        Assert.Throws<InvalidOperationException>(
            () => session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1), draftId));
    }

    [Fact]
    public void Only_The_Manager_Of_The_Drafting_Team_Can_Return_It()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        var draftId = OnlyDraft(session).Id;

        Assert.Throws<ArgumentException>(() => session.ReturnContractDraft(draftId, sellerId));
    }

    [Fact]
    public void Only_The_Author_Can_Withdraw_A_Draft_And_Only_While_It_Awaits_The_Manager()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var otherNegotiatorCode = Register(session, ParticipantRole.Negotiator, buyerId, seed: 3);
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        var draftId = OnlyDraft(session).Id;

        Assert.Throws<ArgumentException>(() => session.WithdrawContractDraft(draftId, otherNegotiatorCode));

        session.WithdrawContractDraft(draftId, negotiatorCode);

        Assert.Equal(ContractDraftStatus.Withdrawn, session.State.ContractDrafts[draftId].Status);
        Assert.Throws<InvalidOperationException>(() => session.WithdrawContractDraft(draftId, negotiatorCode));
    }

    [Fact]
    public void A_Proposal_Without_A_Draft_Serializes_Exactly_As_Before_Drafts_Existed()
    {
        // Хеш записи журнала считается по JSON события (EventLog.ComputeHash). Если бы пустой SourceDraftId
        // попадал в JSON, все заявки в уже записанных журналах перестали бы сходиться с хешем.
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1));
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1), OnlyDraft(session).Id);

        var json = session.Entries.Select(e => e.Change).OfType<ContractProposalSubmitted>()
            .Select(c => System.Text.Json.JsonSerializer.Serialize(c, c.GetType()))
            .ToList();

        Assert.DoesNotContain(nameof(ContractProposalSubmitted.SourceDraftId), json[0]);
        Assert.Contains(nameof(ContractProposalSubmitted.SourceDraftId), json[1]);
    }

    [Fact]
    public void Drafts_Are_Rebuilt_By_Replaying_The_Journal()
    {
        var (session, buyerId, sellerId, negotiatorCode) = StartWithBuyerNegotiator();
        var (buyerProposal, _) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId);
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        session.PrepareContractDraft(buyerProposal, negotiatorCode);
        var drafts = session.State.ContractDrafts.Keys.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList();
        var submitted = session.SubmitContractProposal(buyerProposal, TeamRole.Manager, new Random(1), drafts[0]);
        session.ReturnContractDraft(drafts[1], buyerId, "позже");

        var replayed = new GameSessionState(session.State.Config);
        foreach (var entry in session.Entries)
        {
            entry.Change.Apply(replayed);
        }

        Assert.Equal(ContractDraftStatus.Submitted, replayed.ContractDrafts[drafts[0]].Status);
        Assert.Equal(submitted.ProposalId, replayed.ContractDrafts[drafts[0]].SubmittedProposalId);
        Assert.Equal(ContractDraftStatus.Returned, replayed.ContractDrafts[drafts[1]].Status);
        Assert.Equal("позже", replayed.ContractDrafts[drafts[1]].ReturnReason);
    }
}
