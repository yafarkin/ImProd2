namespace Game.Engine.Tests;

/// <summary>
/// Интеграционные тесты Блока 4.4: полный проход тика поверх уже собранных вместе Блоков 4.1-4.3.
/// </summary>
public class GameSessionRunTickTests
{
    // Стартовые деньги применяются прямо к объекту Team (не через GameSession), потому что тесты
    // этого файла строят фабрики прямым team.BuildFactory(...) ещё в фазе расчёта, до какого-либо
    // AdvancePhase.
    private static GameSession StartSessionWithOneFundedTeam(out Ulid teamId)
    {
        teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            TestGameConfig.Resolved,
            "test",
            endTurn: 999,
            new[]
            {
                new TeamSpec
                {
                    Id = teamId,
                    Name = "Команда А1",
                    SectorId = TestGameConfig.SectorA.Id,
                },
            });

        session.State.Teams[teamId].Credit(1000m);
        return session;
    }

    [Fact]
    public void RunTick_Feeds_Ore_Mined_This_Tick_Into_The_Mill_In_The_Same_Tick()
    {
        var session = StartSessionWithOneFundedTeam(out var teamId);
        var team = session.State.Teams[teamId];
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        mine.Hire(5);
        var mill = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);
        mill.Hire(5);

        session.RunTick(new Random(1));

        // Рудник (уровень 0) должен отработать раньше завода (уровень 1) в этом же тике — иначе
        // заводу нечего было бы перерабатывать, и лист остался бы нулевым.
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Ore)); // вся добытая руда ушла в переработку
        Assert.Equal(2.5m, team.Warehouse.QuantityOf(TestGameConfig.Sheet)); // 5 руды / 2 = 2.5 листа

        // Финансовый шаг уже отработал в этом же RunTick: зарплата (10 рабочих * 5 = 50) списана с баланса.
        Assert.Equal(950m, team.Balance);
        Assert.False(team.Balance < 0);
    }

    [Fact]
    public void RunTick_Across_Several_Turns_Stays_Deterministic_And_The_Journal_Stays_Intact()
    {
        var session = StartSessionWithOneFundedTeam(out var teamId);
        var team = session.State.Teams[teamId];
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        mine.Hire(5);
        var mill = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);
        mill.Hire(5);

        session.RunTick(new Random(1)); // ход 1

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement, ход 2

        session.RunTick(new Random(1)); // ход 2

        Assert.Equal(2, session.State.CurrentTurn);
        Assert.Equal(TurnPhase.Settlement, session.State.CurrentPhase);
        // Ещё 5 руды добыто и переработано за второй ход поверх уже накопленных 2.5 листа.
        Assert.Equal(5m, team.Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.True(session.VerifyIntegrity());
    }

    [Fact]
    public void RunTick_Throws_Outside_The_Settlement_Phase()
    {
        var session = StartSessionWithOneFundedTeam(out _);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision

        Assert.Throws<InvalidOperationException>(() => session.RunTick(new Random(1)));
    }

    [Fact]
    public void RunTick_With_No_Teams_Still_Publishes_The_Market_Update()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "test", endTurn: 999, Array.Empty<TeamSpec>());

        var appended = session.RunTick(new Random(1));

        // Финансы/производство/контракты пропускаются без команд, но рынок (Блок 6.1) — внешняя
        // экономика, она обновляется независимо от того, есть ли вообще участники.
        var update = Assert.IsType<MarketUpdated>(Assert.Single(appended).Change);
        Assert.True(session.State.Market.HasQuote(TestGameConfig.Ore.Id));
        Assert.Equal(update.ElectricityPrice, session.State.Market.ElectricityPrice);
    }
}
