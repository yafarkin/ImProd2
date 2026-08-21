using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Сквозной путь Блока 6.2 через <see cref="GameSession"/>: реальная история контрактов,
/// накопленная через <see cref="GameSession.RunTick"/>, отражается в <see cref="GameSession.GetReputation"/>.
/// </summary>
public class GameSessionReputationTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    private static void ToNextSettlement(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Settlement))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    [Fact]
    public void GetReputation_Reflects_A_Successful_Delivery_Recorded_By_RunTick()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, deliveryTurn: 2, effectiveTurn: 2);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        session.ConfirmContract(result.Contract!.Id, TeamRole.Manager, sellerId);
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);

        ToNextSettlement(session); // ход 2
        session.RunTick(new Random(1));

        var reputation = session.GetReputation(sellerId);
        Assert.Equal(100m, reputation.Percentage);
        Assert.Equal(1, reputation.SampleCount);
    }
}
