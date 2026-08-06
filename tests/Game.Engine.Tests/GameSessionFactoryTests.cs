namespace Game.Engine.Tests;

/// <summary>
/// Постройка фабрик и объявление желаемой численности рабочих через <see cref="GameSession"/> (Блок
/// 7.1, SPEC §5.6). Объявление (<see cref="GameSession.SetWorkerCount"/>) бесплатно и мгновенно —
/// реальный наём/увольнение и разовая плата за него проверены отдельно, на фазе расчёта, в
/// WorkforceStepTests и TickFinanceStepWorkforceTests.
/// </summary>
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
    public void BuildFactory_Throws_For_A_Factory_Whose_Generation_Is_Not_Yet_Unlocked()
    {
        // TestGameConfig.BuildWithGenerationResearch добавляет уровень 2 (coil-plant) поверх
        // обычной цепочки руда(0)/лист(1) — по умолчанию StartingGeneration=1, порогов нет, значит
        // команда никогда не разблокирует поколение 2 сама по себе (Блок 9.2, запрос пользователя:
        // будущие фабрики должны появляться постепенно, а не быть доступны с хода 1).
        var config = TestGameConfig.BuildWithGenerationResearch();
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            new EventLog<GameSessionState>(new GameSessionState(config)), "test", endTurn: 999,
            new[] { new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        var ex = Assert.Throws<ArgumentException>(() => session.BuildFactory(teamId, "coil-plant"));
        Assert.Contains("generation 2", ex.Message);
        Assert.Empty(session.State.Teams[teamId].Factories);
    }

    [Fact]
    public void BuildFactory_Succeeds_Once_The_Required_Generation_Is_Unlocked()
    {
        var config = TestGameConfig.BuildWithGenerationResearch();
        var teamId = Ulid.NewUlid();
        var log = new EventLog<GameSessionState>(new GameSessionState(config));
        var session = GameSession.StartWithEndTurn(
            log, "test", endTurn: 999,
            new[] { new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision
        log.Append(new TeamGenerationAdvanced { Id = Ulid.NewUlid(), TeamId = teamId, NewGeneration = 2 });

        var entry = session.BuildFactory(teamId, "coil-plant");

        Assert.IsType<FactoryBuilt>(entry.Change);
    }

    [Fact]
    public void SetWorkerCount_Declares_The_Desired_Headcount_Without_Charging_Anything_Yet()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        var balanceAfterBuild = session.State.Teams[teamId].Balance;

        var entry = session.SetWorkerCount(teamId, built.FactoryId, 5);

        var set = Assert.IsType<WorkerCountSet>(entry.Change);
        Assert.Equal(5, set.Count);
        var factory = session.State.Teams[teamId].Factories.Single();
        Assert.Equal(5, factory.DesiredWorkers);
        Assert.Equal(0, factory.Workers); // реальный наём — только на фазе расчёта
        Assert.Equal(balanceAfterBuild, session.State.Teams[teamId].Balance); // объявление бесплатно
    }

    [Fact]
    public void SetWorkerCount_Can_Be_Changed_Any_Number_Of_Times_Before_Settlement_For_Free()
    {
        // Пользовательский сценарий: команда несколько раз крутит число туда-сюда за один и тот же
        // ход — ни одно промежуточное значение не должно стоить денег, платится только итог (см.
        // GameSessionRndProgressionTests и WorkforceStepTests — тот же приём и для R&D).
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        var balanceAfterBuild = session.State.Teams[teamId].Balance;

        session.SetWorkerCount(teamId, built.FactoryId, 10); // нанял 10...
        session.SetWorkerCount(teamId, built.FactoryId, 5);  // ...передумал, уволил 5...
        session.SetWorkerCount(teamId, built.FactoryId, 5);  // ...остановился на 5

        Assert.Equal(5, session.State.Teams[teamId].Factories.Single().DesiredWorkers);
        Assert.Equal(balanceAfterBuild, session.State.Teams[teamId].Balance); // ни одно объявление не списывает деньги
    }

    [Fact]
    public void SetWorkerCount_Throws_For_A_Negative_Count()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetWorkerCount(teamId, built.FactoryId, -1));
    }

    [Fact]
    public void SetWorkerCount_Throws_For_An_Unknown_Factory()
    {
        var (session, teamId) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.SetWorkerCount(teamId, Ulid.NewUlid(), 5));
    }

    [Fact]
    public void A_Built_And_Staffed_Factory_Produces_Starting_From_The_Next_Tick()
    {
        var (session, teamId) = StartInDecisionPhase();
        var built = (FactoryBuilt)session.BuildFactory(teamId, TestGameConfig.Mine.Id).Change;
        session.SetWorkerCount(teamId, built.FactoryId, 5);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        Assert.Contains(appended, e => e.Change is WorkersHired hired && hired.Count == 5); // наём settled здесь, разово
        var produced = Assert.IsType<FactoryProduced>(appended.Single(e => e.Change is FactoryProduced).Change);
        Assert.Equal(5m, produced.OutputQuantity); // 5 рабочих, ProductionRate=1, OutputQuantity=1 -> 5 руды
        Assert.Equal(5m, session.State.Teams[teamId].Warehouse.QuantityOf(TestGameConfig.Ore));
    }
}
