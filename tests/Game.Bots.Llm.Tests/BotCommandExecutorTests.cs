using Game.Domain;

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

        var result = Executor.Execute(command, session, teamId);

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Empty(session.State.Teams[teamId].Factories);
    }

    [Fact]
    public void SellFactory_MissingFactoryId_ReturnsDomainError()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand { Kind = BotCommandKind.SellFactory };

        var result = Executor.Execute(command, session, teamId);

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

        var result = Executor.Execute(command, session, teamId);

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

        var result = Executor.Execute(command, session, teamId);

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

        var result = Executor.Execute(command, session, teamId);

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

        var result = Executor.Execute(command, session, teamId);

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

        var result = Executor.Execute(command, session, teamId);

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal(NeedStatus.Withdrawn, session.State.Needs[needId].Status);
    }

    [Fact]
    public void EmergencyPurchase_RequestsVolume()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var command = new BotCommand { Kind = BotCommandKind.EmergencyPurchase, MaterialId = "ore", Volume = 50m };

        var result = Executor.Execute(command, session, teamId);

        Assert.IsType<BotCommandExecutionResult.Success>(result);
        Assert.Equal(50m, session.State.Teams[teamId].PendingEmergencyPurchaseVolumeByMaterial["ore"]);
    }
}
