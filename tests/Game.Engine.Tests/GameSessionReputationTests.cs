using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Сквозной путь Блока 6.2 через <see cref="GameSession"/>: реальная история контрактов,
/// накопленная через <see cref="GameSession.RunTick"/>, отражается и в <see cref="GameSession.GetReputation"/>,
/// и — по SPEC §5.9 — в ставке по кредиту на последующих ходах.
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
        session.ConfirmContract(result.Contract!.Id, TeamRole.Manager);
        session.State.Teams[sellerId].Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);

        ToNextSettlement(session); // ход 2
        session.RunTick(new Random(1));

        var reputation = session.GetReputation(sellerId);
        Assert.Equal(100m, reputation.Percentage);
        Assert.Equal(1, reputation.SampleCount);
    }

    [Fact]
    public void A_Delivery_Miss_Beyond_Warmup_Raises_The_Sellers_Loan_Rate_On_A_Later_Tick()
    {
        // TestGameConfig: WarmupTurns=3, BaseLoanInterestRate=0.05, LoanInterestRateGrowthPerUnitBorrowed=0,
        // MaxReputationRatePenalty=0.1 — с ростом долга ставка не меняется, поэтому разница в
        // Rate между двумя прогонами объясняется исключительно репутацией.
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, deliveryTurn: 4, effectiveTurn: 1);
        var result = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1));
        session.ConfirmContract(result.Contract!.Id, TeamRole.Manager);
        // продавцу нечем поставить — на ходу 4 (уже после «пристрелочных» ходов 1-3) будет Delivery Miss

        LoanInterestCharged? RunTurnAndGetSellerInterest()
        {
            ToNextSettlement(session);
            var appended = session.RunTick(new Random(1));
            foreach (var entry in appended)
            {
                if (entry.Change is LoanInterestCharged charged && charged.TeamId == sellerId)
                {
                    return charged;
                }
            }

            return null;
        }

        var turn2Interest = RunTurnAndGetSellerInterest(); // до срыва
        var turn3Interest = RunTurnAndGetSellerInterest(); // срыв ещё не случился (наступит на этом же ходу, после финансов)
        Assert.Equal(0.05m, turn2Interest!.Rate);
        Assert.Equal(0.05m, turn3Interest!.Rate);

        var turn4Interest = RunTurnAndGetSellerInterest(); // финансы хода 4 всё ещё до Delivery Miss хода 4
        Assert.Equal(0.05m, turn4Interest!.Rate);
        Assert.True(session.State.Market.HasQuote(TestGameConfig.Ore.Id)); // сквозная проверка, что тик вообще досчитал до конца

        var reputationAfterMiss = session.GetReputation(sellerId);
        Assert.Equal(0m, reputationAfterMiss.Percentage);

        var turn5Interest = RunTurnAndGetSellerInterest(); // теперь репутация уже подпорчена
        Assert.Equal(0.15m, turn5Interest!.Rate); // 0.05 + 0.1 (вся надбавка при 0% репутации)
    }
}
