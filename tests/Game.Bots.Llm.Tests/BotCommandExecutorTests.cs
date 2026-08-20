using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm.Tests;

/// <summary>
/// <see cref="BotCommandExecutor"/> напрямую для команд, добавленных 2026-08-16 (запрос
/// пользователя: «простые действия — давай добавим»: SellFactory, SetFactoryAllocationShare,
/// PostNeed/WithdrawNeed, EmergencyPurchase) — на реальной сессии, без единого обращения к LLM.
/// </summary>
public sealed class BotCommandExecutorTests
{
    private static readonly BotCommandExecutor Executor = new();

    [Fact]
    public void SellFactory_LiquidatesExistingFactory()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;
        var command = new BotCommand { Kind = BotCommandKind.SellFactory, FactoryId = factoryId };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Empty(session.State.Teams[teamId].Factories);
    }

    [Fact]
    public void SellFactory_MissingFactoryId_ReturnsDomainError()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand { Kind = BotCommandKind.SellFactory };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        var error = Assert.IsType<BotCommandExecutionResult.DomainError>(result);
        Assert.Contains("factoryId", error.Message);
    }

    [Fact]
    public void SetFactoryAllocationShare_UpdatesExistingFactory()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;
        var command = new BotCommand { Kind = BotCommandKind.SetFactoryAllocationShare, FactoryId = factoryId, Share = 0.6m };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal(0.6m, session.State.Teams[teamId].Factories[0].AllocationShare);
    }

    [Fact]
    public void PostNeed_PublishesActivePosting()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand
        {
            Kind = BotCommandKind.PostNeed,
            MaterialId = "ore",
            Direction = "surplus",
            VolumeOrder = "medium",
            Comment = "have extra ore this week",
        };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        var posting = Assert.Single(session.State.Needs.Values);
        Assert.Equal(teamId, posting.TeamId);
        Assert.Equal("ore", posting.Material.Id);
        Assert.Equal(NeedDirection.Surplus, posting.Direction);
        Assert.Equal(NeedVolumeOrder.Medium, posting.VolumeOrder);
        Assert.Equal("have extra ore this week", posting.Comment);
    }

    [Theory]
    [InlineData("SURPLUS")]
    [InlineData("Deficit")]
    public void PostNeed_DirectionIsCaseInsensitive(string direction)
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand
        {
            Kind = BotCommandKind.PostNeed, MaterialId = "ore", Direction = direction, VolumeOrder = "small",
        };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
    }

    [Fact]
    public void PostNeed_UnknownDirection_ReturnsDomainError()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand
        {
            Kind = BotCommandKind.PostNeed, MaterialId = "ore", Direction = "excess", VolumeOrder = "small",
        };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        var error = Assert.IsType<BotCommandExecutionResult.DomainError>(result);
        Assert.Contains("direction", error.Message);
    }

    [Fact]
    public void WithdrawNeed_MarksOwnPostingWithdrawn()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.PostNeed(teamId, "ore", NeedDirection.Surplus, NeedVolumeOrder.Small, null);
        var needId = session.State.Needs.Values.Single().Id;
        var command = new BotCommand { Kind = BotCommandKind.WithdrawNeed, NeedId = needId };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal(NeedStatus.Withdrawn, session.State.Needs[needId].Status);
    }

    [Fact]
    public void EmergencyPurchase_RequestsVolume()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand { Kind = BotCommandKind.EmergencyPurchase, MaterialId = "ore", Volume = 50m };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal(50m, session.State.Teams[teamId].PendingEmergencyPurchaseVolumeByMaterial["ore"]);
    }

    [Fact]
    public void PostSellOffer_PublishesAnOpenOffer()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand
        {
            Kind = BotCommandKind.PostSellOffer, MaterialId = "ore", Volume = 20m, MinPrice = 5m, MaxPrice = 8m,
        };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        var offer = Assert.Single(session.State.TradeOffers.Values);
        Assert.Equal(teamId, offer.TeamId);
        Assert.Equal(TradeOfferDirection.Sell, offer.Direction);
        Assert.Equal(ContractType.Spot, offer.Type);
        Assert.Equal(20m, offer.Volume);
        Assert.Equal(5m, offer.MinPrice);
        Assert.Equal(8m, offer.MaxPrice);
    }

    [Fact]
    public void PostBuyOffer_Recurring_PublishesARecurringOffer()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand
        {
            Kind = BotCommandKind.PostBuyOffer, MaterialId = "ore", Volume = 20m, MinPrice = 5m, MaxPrice = 8m, Recurring = true,
        };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        var offer = Assert.Single(session.State.TradeOffers.Values);
        Assert.Equal(TradeOfferDirection.Buy, offer.Direction);
        Assert.Equal(ContractType.Recurring, offer.Type);
    }

    [Fact]
    public void PostSellOffer_MissingPriceRange_ReturnsDomainError()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand { Kind = BotCommandKind.PostSellOffer, MaterialId = "ore", Volume = 20m };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        var error = Assert.IsType<BotCommandExecutionResult.DomainError>(result);
        Assert.Contains("minPrice", error.Message);
    }

    [Fact]
    public void WithdrawTradeOffer_MarksOwnOfferWithdrawn()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var posted = session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 20m, 5m, 8m);
        var offerId = ((TradeOfferPosted)posted.Change).TradeOfferId;
        var command = new BotCommand { Kind = BotCommandKind.WithdrawTradeOffer, TradeOfferId = offerId };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal(TradeOfferStatus.Withdrawn, session.State.TradeOffers[offerId].Status);
    }

    [Fact]
    public void FulfillTradeOffer_FormsAndConfirmsAContractAtTheChosenPrice()
    {
        var (session, sellerId, buyerId) = TestSession.StartTwoTeamSession();
        var posted = session.PostTradeOffer(sellerId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 20m, 5m, 8m);
        var offerId = ((TradeOfferPosted)posted.Change).TradeOfferId;
        var command = new BotCommand { Kind = BotCommandKind.FulfillTradeOffer, TradeOfferId = offerId, Volume = 15m, UnitPrice = 6m };

        var result = Executor.Execute(command, session, buyerId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal(TradeOfferStatus.Fulfilled, session.State.TradeOffers[offerId].Status);
        var contract = Assert.Single(session.State.Contracts.Values);
        Assert.Equal(sellerId, contract.SellerTeamId);
        Assert.Equal(buyerId, contract.BuyerTeamId);
        Assert.Equal(15m, contract.Terms.Volume);
        Assert.Equal(6m, contract.Terms.UnitPrice);
        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void FulfillTradeOffer_PriceOutsideRange_ReturnsDomainError()
    {
        var (session, sellerId, buyerId) = TestSession.StartTwoTeamSession();
        var posted = session.PostTradeOffer(sellerId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 20m, 5m, 8m);
        var offerId = ((TradeOfferPosted)posted.Change).TradeOfferId;
        var command = new BotCommand { Kind = BotCommandKind.FulfillTradeOffer, TradeOfferId = offerId, Volume = 15m, UnitPrice = 20m };

        var result = Executor.Execute(command, session, buyerId, new Random(1));

        var error = Assert.IsType<BotCommandExecutionResult.DomainError>(result);
        Assert.Contains("unitPrice", error.Message);
        Assert.Equal(TradeOfferStatus.Open, session.State.TradeOffers[offerId].Status);
    }

    [Fact]
    public void FulfillTradeOffer_OwnOffer_ReturnsDomainError()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var posted = session.PostTradeOffer(teamId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 20m, 5m, 8m);
        var offerId = ((TradeOfferPosted)posted.Change).TradeOfferId;
        var command = new BotCommand { Kind = BotCommandKind.FulfillTradeOffer, TradeOfferId = offerId, Volume = 15m, UnitPrice = 6m };

        var result = Executor.Execute(command, session, teamId, new Random(1));

        var error = Assert.IsType<BotCommandExecutionResult.DomainError>(result);
        Assert.Contains("own trade offer", error.Message);
    }

    [Fact]
    public void FulfillTradeOffer_VolumeAndUnitPriceOmitted_DefaultsToFullVolumeAndMidpointPrice()
    {
        // Прямой запрос пользователя 2026-08-20, по следам _2bot_gpt_oss_20b_2stage_v4: 37/37 живых
        // попыток fulfillTradeOffer в одном прогоне провалились ровно на этой паре полей — модель
        // называла tradeOfferId верно каждый раз, но систематически теряла volume или unitPrice (то
        // одно, то другое). tradeOfferId остался единственным обязательным полем.
        var (session, sellerId, buyerId) = TestSession.StartTwoTeamSession();
        var posted = session.PostTradeOffer(sellerId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 20m, 5m, 8m);
        var offerId = ((TradeOfferPosted)posted.Change).TradeOfferId;
        var command = new BotCommand { Kind = BotCommandKind.FulfillTradeOffer, TradeOfferId = offerId };

        var result = Executor.Execute(command, session, buyerId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        var contract = Assert.Single(session.State.Contracts.Values);
        Assert.Equal(20m, contract.Terms.Volume); // вся заявка целиком
        Assert.Equal(6.5m, contract.Terms.UnitPrice); // середина 5-8
    }

    [Fact]
    public void FulfillTradeOffer_OnlyUnitPriceOmitted_KeepsTheExplicitVolume()
    {
        var (session, sellerId, buyerId) = TestSession.StartTwoTeamSession();
        var posted = session.PostTradeOffer(sellerId, TradeOfferDirection.Sell, "ore", ContractType.Spot, 20m, 5m, 8m);
        var offerId = ((TradeOfferPosted)posted.Change).TradeOfferId;
        var command = new BotCommand { Kind = BotCommandKind.FulfillTradeOffer, TradeOfferId = offerId, Volume = 12m };

        var result = Executor.Execute(command, session, buyerId, new Random(1));

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        var contract = Assert.Single(session.State.Contracts.Values);
        Assert.Equal(12m, contract.Terms.Volume);
        Assert.Equal(6.5m, contract.Terms.UnitPrice);
    }

    [Fact]
    public void FulfillTradeOffer_TradeOfferIdMissing_ReturnsDomainError()
    {
        var (session, _, buyerId) = TestSession.StartTwoTeamSession();
        var command = new BotCommand { Kind = BotCommandKind.FulfillTradeOffer, Volume = 15m, UnitPrice = 6m };

        var result = Executor.Execute(command, session, buyerId, new Random(1));

        var error = Assert.IsType<BotCommandExecutionResult.DomainError>(result);
        Assert.Contains("tradeOfferId", error.Message);
    }
}
