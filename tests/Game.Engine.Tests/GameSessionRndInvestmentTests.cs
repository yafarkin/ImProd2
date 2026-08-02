namespace Game.Engine.Tests;

/// <summary>R&amp;D-вложения в фабрику через <see cref="GameSession"/> (Блок 9.2, SPEC §5.8).</summary>
public class GameSessionRndInvestmentTests
{
    // Пороги TestGameConfig.Resolved.Raw.Rnd: { 100m, 300m } — 1->2, 2->3.
    private static (GameSession Session, Ulid TeamId, Ulid FactoryId) StartInDecisionPhaseWithFactory()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;

        return (session, teamId, built.FactoryId);
    }

    [Fact]
    public void InvestInRnd_Appends_Only_RndInvested_Below_The_Threshold()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();

        var entries = session.InvestInRnd(teamId, factoryId, 50m);

        var invested = Assert.IsType<RndInvested>(Assert.Single(entries).Change);
        Assert.Equal(50m, invested.Amount);
        Assert.Equal(50m, session.State.Teams[teamId].Factories.Single().RndInvestment);
        Assert.Equal(1, session.State.Teams[teamId].Factories.Single().Level);
    }

    [Fact]
    public void InvestInRnd_Appends_FactoryLevelAdvanced_At_The_Threshold()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();

        var entries = session.InvestInRnd(teamId, factoryId, 100m);

        Assert.Equal(2, entries.Count);
        Assert.IsType<RndInvested>(entries[0].Change);
        var levelAdvanced = Assert.IsType<FactoryLevelAdvanced>(entries[1].Change);
        Assert.Equal(2, levelAdvanced.NewLevel);
        Assert.Equal(2, session.State.Teams[teamId].Factories.Single().Level);
    }

    [Fact]
    public void InvestInRnd_Appends_One_FactoryLevelAdvanced_Per_Threshold_Crossed_At_Once()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();

        var entries = session.InvestInRnd(teamId, factoryId, 400m); // покрывает оба порога сразу

        Assert.Equal(3, entries.Count);
        Assert.Equal(2, Assert.IsType<FactoryLevelAdvanced>(entries[1].Change).NewLevel);
        Assert.Equal(3, Assert.IsType<FactoryLevelAdvanced>(entries[2].Change).NewLevel);
    }

    [Fact]
    public void InvestInRnd_Throws_For_An_Unknown_Team()
    {
        var (session, _, factoryId) = StartInDecisionPhaseWithFactory();

        Assert.Throws<ArgumentException>(() => session.InvestInRnd(Ulid.NewUlid(), factoryId, 50m));
    }

    [Fact]
    public void InvestInRnd_Throws_For_An_Unknown_Factory()
    {
        var (session, teamId, _) = StartInDecisionPhaseWithFactory();

        Assert.Throws<ArgumentException>(() => session.InvestInRnd(teamId, Ulid.NewUlid(), 50m));
    }

    [Fact]
    public void InvestInRnd_Throws_For_A_NonPositive_Amount()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.InvestInRnd(teamId, factoryId, 0m));
    }

    [Fact]
    public void InvestInRnd_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId, factoryId) = StartInDecisionPhaseWithFactory();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement

        Assert.Throws<InvalidOperationException>(() => session.InvestInRnd(teamId, factoryId, 50m));
    }
}
