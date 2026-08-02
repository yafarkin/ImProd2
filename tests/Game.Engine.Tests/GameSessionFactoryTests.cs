namespace Game.Engine.Tests;

/// <summary>Постройка фабрик и наём/увольнение рабочих через <see cref="GameSession"/> (Блок 7.1, SPEC §5.6).</summary>
public class GameSessionFactoryTests
{
    private static (GameSession Session, Ulid TeamId) StartInDecisionPhase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam();
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        return (session, teamId);
    }

    [Fact]
    public void BuildFactory_Adds_A_Factory_With_No_Workers_And_The_Default_Recipe()
    {
        var (session, teamId) = StartInDecisionPhase();

        var entry = session.BuildFactory(teamId, TestGameConfig.Mine.Id);

        var built = Assert.IsType<FactoryBuilt>(entry.Change);
        Assert.Equal("ore-mining", built.RecipeId);
        Assert.Equal(100m, built.Cost); // TestGameConfig: BuildCost заглушки = 100

        var factory = Assert.Single(session.State.Teams[teamId].Factories);
        Assert.Equal(0, factory.Workers);
        Assert.Equal(100_000m - 100m, session.State.Teams[teamId].Balance);
    }

    [Fact]
    public void BuildFactory_Throws_For_An_Unknown_Team()
    {
        var (session, _) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine.Id));
    }

    [Fact]
    public void BuildFactory_Throws_For_An_Unknown_Factory_Definition()
    {
        var (session, teamId) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.BuildFactory(teamId, "does-not-exist"));
    }

    [Fact]
    public void BuildFactory_Throws_For_An_Unknown_Recipe()
    {
        var (session, teamId) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.BuildFactory(teamId, TestGameConfig.Mine.Id, "no-such-recipe"));
    }

    [Fact]
    public void BuildFactory_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(); // Settlement, ход 1

        Assert.Throws<InvalidOperationException>(() => session.BuildFactory(teamId, TestGameConfig.Mine.Id));
    }

    [Fact]
    public void HireWorkers_Charges_The_Configured_Cost_Per_Worker()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        var balanceAfterBuild = session.State.Teams[teamId].Balance;

        var entry = session.HireWorkers(teamId, built.FactoryId, 5);

        var hired = Assert.IsType<WorkersHired>(entry.Change);
        Assert.Equal(5 * 50m, hired.Cost); // TestGameConfig: HireCostPerWorker = 50
        Assert.Equal(5, session.State.Teams[teamId].Factories.Single().Workers);
        Assert.Equal(balanceAfterBuild - hired.Cost, session.State.Teams[teamId].Balance);
    }

    [Fact]
    public void HireWorkers_Throws_For_An_Unknown_Factory()
    {
        var (session, teamId) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.HireWorkers(teamId, Ulid.NewUlid(), 5));
    }

    [Fact]
    public void FireWorkers_Charges_The_Configured_Cost_Per_Worker()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.HireWorkers(teamId, built.FactoryId, 5);
        var balanceAfterHire = session.State.Teams[teamId].Balance;

        var entry = session.FireWorkers(teamId, built.FactoryId, 2);

        var fired = Assert.IsType<WorkersFired>(entry.Change);
        Assert.Equal(2 * 30m, fired.Cost); // TestGameConfig: FireCostPerWorker = 30
        Assert.Equal(3, session.State.Teams[teamId].Factories.Single().Workers);
        Assert.Equal(balanceAfterHire - fired.Cost, session.State.Teams[teamId].Balance);
    }

    [Fact]
    public void FireWorkers_Throws_When_Firing_More_Than_Currently_Employed()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.HireWorkers(teamId, built.FactoryId, 2);

        Assert.Throws<InvalidOperationException>(() => session.FireWorkers(teamId, built.FactoryId, 3));
    }

    [Fact]
    public void A_Built_And_Staffed_Factory_Produces_Starting_From_The_Next_Tick()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.HireWorkers(teamId, built.FactoryId, 5);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        var produced = Assert.IsType<FactoryProduced>(appended.Single(e => e.Change is FactoryProduced).Change);
        Assert.Equal(5m, produced.OutputQuantity); // 5 рабочих, ProductionRate=1, OutputQuantity=1 -> 5 руды
        Assert.Equal(5m, session.State.Teams[teamId].Warehouse.QuantityOf(TestGameConfig.Ore));
    }
}
