using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Доска публичных заявок через <see cref="GameSession"/> (запрос пользователя 2026-08-17, TODO #20).</summary>
public class GameSessionTradeOffersTests
{
    private static void ToDecisionPhase(GameSession session) => session.AdvancePhase(PhaseTransitionTrigger.Timer);

    /// <summary>Крутит фазы вперёд, пока сессия не окажется в фазе решений уже следующего хода.</summary>
    private static void ToNextDecision(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Decision))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    [Fact]
    public void PostTradeOffer_Appends_A_TradeOfferPosted_And_The_Offer_Is_Open()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        ToDecisionPhase(session);

        var entry = session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m);

        var posted = Assert.IsType<TradeOfferPosted>(entry.Change);
        var offer = session.State.TradeOffers[posted.TradeOfferId];
        Assert.Equal(teamId, offer.TeamId);
        Assert.Equal(TradeOfferDirection.Sell, offer.Direction);
        Assert.Equal(ContractType.Spot, offer.Type);
        Assert.Equal(10m, offer.Volume);
        Assert.Equal(5m, offer.MinPrice);
        Assert.Equal(8m, offer.MaxPrice);
        Assert.Equal(TradeOfferStatus.Open, offer.Status);
        Assert.True(offer.IsOpenOn(session.State.CurrentTurn));
    }

    [Fact]
    public void PostTradeOffer_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(); // сессия открывается в фазе расчёта

        Assert.Throws<InvalidOperationException>(
            () => session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m));
    }

    [Fact]
    public void PostTradeOffer_Throws_For_An_Unknown_Team()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();
        ToDecisionPhase(session);

        Assert.Throws<ArgumentException>(
            () => session.PostTradeOffer(Ulid.NewUlid(), TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m));
    }

    [Fact]
    public void PostTradeOffer_Throws_For_An_Unknown_Material()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        ToDecisionPhase(session);

        Assert.Throws<ArgumentException>(
            () => session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "no-such-material", ContractType.Spot, 10m, 5m, 8m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PostTradeOffer_Throws_For_NonPositive_Volume(decimal volume)
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        ToDecisionPhase(session);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, volume, 5m, 8m));
    }

    [Fact]
    public void PostTradeOffer_Throws_When_MaxPrice_Below_MinPrice()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        ToDecisionPhase(session);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 8m, 5m));
    }

    [Fact]
    public void WithdrawTradeOffer_Marks_The_Offer_Withdrawn()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        ToDecisionPhase(session);
        var posted = (TradeOfferPosted)session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m).Change;

        session.WithdrawTradeOffer(teamId, posted.TradeOfferId);

        Assert.Equal(TradeOfferStatus.Withdrawn, session.State.TradeOffers[posted.TradeOfferId].Status);
        Assert.False(session.State.TradeOffers[posted.TradeOfferId].IsOpenOn(session.State.CurrentTurn));
    }

    [Fact]
    public void WithdrawTradeOffer_Throws_For_A_NonOwning_Team()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var posted = (TradeOfferPosted)session.PostTradeOffer(sellerId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m).Change;

        Assert.Throws<ArgumentException>(() => session.WithdrawTradeOffer(buyerId, posted.TradeOfferId));
    }

    [Fact]
    public void WithdrawTradeOffer_Throws_When_Already_Withdrawn()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        ToDecisionPhase(session);
        var posted = (TradeOfferPosted)session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m).Change;
        session.WithdrawTradeOffer(teamId, posted.TradeOfferId);

        Assert.Throws<InvalidOperationException>(() => session.WithdrawTradeOffer(teamId, posted.TradeOfferId));
    }

    [Fact]
    public void MarkTradeOfferFulfilled_Marks_The_Offer_Fulfilled()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var posted = (TradeOfferPosted)session.PostTradeOffer(sellerId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m).Change;

        session.MarkTradeOfferFulfilled(posted.TradeOfferId, buyerId);

        Assert.Equal(TradeOfferStatus.Fulfilled, session.State.TradeOffers[posted.TradeOfferId].Status);
    }

    [Fact]
    public void MarkTradeOfferFulfilled_Throws_For_The_Posting_Teams_Own_Offer()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        ToDecisionPhase(session);
        var posted = (TradeOfferPosted)session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m).Change;

        Assert.Throws<ArgumentException>(() => session.MarkTradeOfferFulfilled(posted.TradeOfferId, teamId));
    }

    [Fact]
    public void MarkTradeOfferFulfilled_Throws_When_Already_Fulfilled()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var posted = (TradeOfferPosted)session.PostTradeOffer(sellerId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m).Change;
        session.MarkTradeOfferFulfilled(posted.TradeOfferId, buyerId);

        Assert.Throws<InvalidOperationException>(() => session.MarkTradeOfferFulfilled(posted.TradeOfferId, buyerId));
    }

    [Fact]
    public void MarkTradeOfferFulfilled_Throws_After_The_Offer_Expires()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        ToDecisionPhase(session);
        var posted = (TradeOfferPosted)session.PostTradeOffer(sellerId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 10m, 5m, 8m).Change;
        var offer = session.State.TradeOffers[posted.TradeOfferId];

        // Заявка живёт TradeOffer.MaxAgeInTurns (3) ходов, включая ход публикации — гоним сессию за этот предел.
        for (var i = 0; i < TradeOffer.MaxAgeInTurns; i++)
        {
            ToNextDecision(session);
        }

        Assert.False(offer.IsOpenOn(session.State.CurrentTurn));
        Assert.Throws<InvalidOperationException>(() => session.MarkTradeOfferFulfilled(posted.TradeOfferId, buyerId));
    }
}
