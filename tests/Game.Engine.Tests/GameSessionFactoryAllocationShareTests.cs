namespace Game.Engine.Tests;

/// <summary>Доля фабрики при разборе дефицитного сырья через <see cref="GameSession"/> (запрос пользователя «как указать, какое количество/% отправить на следующую фабрику»).</summary>
public class GameSessionFactoryAllocationShareTests
{
    private static (GameSession Session, Ulid TeamId) StartInDecisionPhase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        return (session, teamId);
    }

    [Fact]
    public void SetFactoryAllocationShare_Changes_The_Factorys_Share()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;

        var entry = session.SetFactoryAllocationShare(teamId, built.FactoryId, 60m);

        var changed = Assert.IsType<FactoryAllocationShareSet>(entry.Change);
        Assert.Equal(60m, changed.Share);
        Assert.Equal(60m, session.State.Teams[teamId].Factories.Single().AllocationShare);
    }

    [Fact]
    public void SetFactoryAllocationShare_Throws_For_An_Unknown_Factory()
    {
        var (session, teamId) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.SetFactoryAllocationShare(teamId, Ulid.NewUlid(), 50m));
    }

    [Fact]
    public void SetFactoryAllocationShare_Throws_For_A_Negative_Share()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetFactoryAllocationShare(teamId, built.FactoryId, -1m));
    }

    [Fact]
    public void SetFactoryAllocationShare_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement

        Assert.Throws<InvalidOperationException>(() => session.SetFactoryAllocationShare(teamId, built.FactoryId, 50m));
    }
}
