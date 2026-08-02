using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Доска потребностей через <see cref="GameSession"/> (Блок 9.4, SPEC §9.2).</summary>
public class GameSessionNeedsTests
{
    [Fact]
    public void PostNeed_Appends_A_NeedPosted_And_The_Posting_Is_Active()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();

        var entry = session.PostNeed(teamId, "ore", NeedDirection.Deficit, NeedVolumeOrder.Medium, "срочно нужна руда");

        var posted = Assert.IsType<NeedPosted>(entry.Change);
        var posting = session.State.Needs[posted.NeedId];
        Assert.Equal(teamId, posting.TeamId);
        Assert.Equal(NeedDirection.Deficit, posting.Direction);
        Assert.Equal(NeedVolumeOrder.Medium, posting.VolumeOrder);
        Assert.Equal("срочно нужна руда", posting.Comment);
        Assert.Equal(NeedStatus.Active, posting.Status);
    }

    [Fact]
    public void PostNeed_Succeeds_Outside_The_Decision_Phase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(); // Settlement, ход 1

        var entry = session.PostNeed(teamId, "ore", NeedDirection.Surplus, NeedVolumeOrder.Small, null);

        Assert.IsType<NeedPosted>(entry.Change);
    }

    [Fact]
    public void PostNeed_Throws_For_An_Unknown_Team()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentException>(
            () => session.PostNeed(Ulid.NewUlid(), "ore", NeedDirection.Deficit, NeedVolumeOrder.Small, null));
    }

    [Fact]
    public void PostNeed_Throws_For_An_Unknown_Material()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentException>(
            () => session.PostNeed(teamId, "no-such-material", NeedDirection.Deficit, NeedVolumeOrder.Small, null));
    }

    [Fact]
    public void WithdrawNeed_Marks_The_Posting_Withdrawn()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        var posted = (NeedPosted)session.PostNeed(teamId, "ore", NeedDirection.Deficit, NeedVolumeOrder.Small, null).Change;

        session.WithdrawNeed(teamId, posted.NeedId);

        Assert.Equal(NeedStatus.Withdrawn, session.State.Needs[posted.NeedId].Status);
    }

    [Fact]
    public void WithdrawNeed_Throws_For_A_NonOwning_Team()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        var posted = (NeedPosted)session.PostNeed(buyerId, "ore", NeedDirection.Deficit, NeedVolumeOrder.Small, null).Change;

        Assert.Throws<ArgumentException>(() => session.WithdrawNeed(sellerId, posted.NeedId));
    }

    [Fact]
    public void WithdrawNeed_Throws_For_An_Unknown_Posting()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Throws<ArgumentException>(() => session.WithdrawNeed(teamId, Ulid.NewUlid()));
    }

    [Fact]
    public void WithdrawNeed_Throws_When_Already_Withdrawn()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        var posted = (NeedPosted)session.PostNeed(teamId, "ore", NeedDirection.Deficit, NeedVolumeOrder.Small, null).Change;
        session.WithdrawNeed(teamId, posted.NeedId);

        Assert.Throws<InvalidOperationException>(() => session.WithdrawNeed(teamId, posted.NeedId));
    }
}
