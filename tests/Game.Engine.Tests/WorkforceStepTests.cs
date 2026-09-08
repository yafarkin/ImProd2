namespace Game.Engine.Tests;

/// <summary>
/// Приведение фактической численности рабочих фабрики к объявленной (<see
/// cref="Domain.Factory.DesiredWorkers"/>) на фазе расчёта — один раз за ход, по итоговой разнице
/// (SPEC §5.6, тот же приём, что и <see cref="RndInvestmentStep"/>). Объявление (<see
/// cref="GameSession.SetWorkerCount"/>) — отдельно, в GameSessionFactoryTests; сборка в
/// TickFinanceStep — в TickFinanceStepWorkforceTests.
/// </summary>
public class WorkforceStepTests
{
    private static readonly Config.Economy.WorkerProductivityConfig WorkerConfig = TestGameConfig.Resolved.Raw.WorkerProductivity;

    /// <summary>Тот же конфиг, но с реалистично узким пределом найма за ход (в поставке — 5, docs/TODO.md №25).</summary>
    private static Config.Economy.WorkerProductivityConfig WithHireLimit(int maxHiresPerTurn) =>
        WorkerConfig with { MaxHiresPerTurn = maxHiresPerTurn };

    [Fact]
    public void Run_Returns_Null_When_The_Desired_Count_Matches_The_Current_One()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine); // Workers=0, DesiredWorkers=0

        var change = WorkforceStep.Run(team.Id, factory, WorkerConfig);

        Assert.Null(change);
    }

    [Fact]
    public void Run_Returns_WorkersHired_Charging_The_Hire_Cost_For_The_Whole_Difference()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetDesiredWorkers(5);

        var change = WorkforceStep.Run(team.Id, factory, WorkerConfig);

        var hired = Assert.IsType<WorkersHired>(change);
        Assert.Equal(5, hired.Count);
        Assert.Equal(5 * 50m, hired.Cost); // TestGameConfig: HireCostPerWorker = 50
    }

    [Fact]
    public void Run_Returns_WorkersFired_Charging_The_Fire_Cost_For_The_Whole_Difference()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(5); // подтягивает DesiredWorkers до 5 заодно (см. doc-comment Factory.Hire)
        factory.SetDesiredWorkers(2);

        var change = WorkforceStep.Run(team.Id, factory, WorkerConfig);

        var fired = Assert.IsType<WorkersFired>(change);
        Assert.Equal(3, fired.Count);
        Assert.Equal(3 * 30m, fired.Cost); // TestGameConfig: FireCostPerWorker = 30
    }

    [Fact]
    public void Run_Ignores_How_Many_Times_The_Desired_Count_Changed_Before_Settlement_Charging_Once_For_The_Net_Difference()
    {
        // Пользовательский сценарий: нанял 10 (5*50), передумал, уволил до 3, потом снова до 5 —
        // должно списаться один раз, за итоговую разницу с нуля, а не за сумму промежуточных шагов.
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetDesiredWorkers(10);
        factory.SetDesiredWorkers(3);
        factory.SetDesiredWorkers(5);

        var change = WorkforceStep.Run(team.Id, factory, WorkerConfig);

        var hired = Assert.IsType<WorkersHired>(change);
        Assert.Equal(5, hired.Count);
        Assert.Equal(5 * 50m, hired.Cost);
    }

    [Fact]
    public void Applying_The_Returned_Change_End_To_End_Updates_Workers_And_Balance()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetDesiredWorkers(4);
        team.Credit(1000m);

        var change = WorkforceStep.Run(team.Id, factory, WorkerConfig);
        log.Append(change!);

        Assert.Equal(4, factory.Workers);
        Assert.Equal(4, factory.DesiredWorkers);
        Assert.Equal(1000m - 4 * 50m, team.Balance);
        Assert.True(log.VerifyIntegrity());
    }

    /// <summary>
    /// Наём инертен: за один ход фабрика берёт не больше предела, остальное остаётся объявленным и
    /// добирается следующими ходами (docs/TODO.md №25). Платим ровно за фактически нанятых — иначе
    /// команда платила бы вперёд за людей, которые ещё не вышли.
    /// </summary>
    [Fact]
    public void Run_Hires_No_More_Than_The_Per_Turn_Limit_And_Charges_Only_For_Those_Actually_Hired()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill); // передел, не добыча
        factory.SetDesiredWorkers(12);

        var hired = Assert.IsType<WorkersHired>(WorkforceStep.Run(team.Id, factory, WithHireLimit(5)));

        Assert.Equal(5, hired.Count);
        Assert.Equal(5 * 50m, hired.Cost);
    }

    /// <summary>
    /// Остаток не теряется и не требует повторного объявления: <see cref="Domain.Factory.DesiredWorkers"/>
    /// хранит замысел, а не остаток, — за три хода с пределом 5 фабрика доходит ровно до 12 и
    /// останавливается.
    /// </summary>
    [Fact]
    public void The_Remainder_Is_Carried_Over_Without_Re_Declaring_Until_The_Target_Is_Reached()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);
        factory.SetDesiredWorkers(12);
        var config = WithHireLimit(5);

        var hiredPerTurn = new List<int>();
        for (var turn = 0; turn < 4; turn++)
        {
            if (WorkforceStep.Run(team.Id, factory, config) is not WorkersHired hired)
            {
                break;
            }

            hiredPerTurn.Add(hired.Count);
            factory.Hire(hired.Count);
        }

        Assert.Equal(new[] { 5, 5, 2 }, hiredPerTurn);
        Assert.Equal(12, factory.Workers);
        Assert.Equal(12, factory.DesiredWorkers);
        // Четвёртый ход уже ничего не делает — расхождения нет.
        Assert.Null(WorkforceStep.Run(team.Id, factory, config));
    }

    /// <summary>
    /// Добыча (выход рецепта уровня 0) нанимает мгновенно, в обход предела: неквалифицированный труд
    /// выходит на смену сразу, в отличие от ролей выше по цепочке (docs/TODO.md №25).
    /// </summary>
    [Fact]
    public void Raw_Extraction_Hires_The_Whole_Crew_At_Once_Ignoring_The_Limit()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        mine.SetDesiredWorkers(12);

        var hired = Assert.IsType<WorkersHired>(WorkforceStep.Run(team.Id, mine, WithHireLimit(5)));

        Assert.Equal(12, hired.Count);
        Assert.True(WorkforceStep.IsInstantHiring(mine));
        Assert.False(WorkforceStep.IsInstantHiring(team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill)));
    }

    /// <summary>
    /// Увольнение под предел не подпадает — вся асимметрия механики в этом: нанимать долго,
    /// расстаться можно в один ход (и потому дороже за человека).
    /// </summary>
    [Fact]
    public void Firing_Is_Never_Throttled_By_The_Hire_Limit()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);
        factory.Hire(12);
        factory.SetDesiredWorkers(0);

        var fired = Assert.IsType<WorkersFired>(WorkforceStep.Run(team.Id, factory, WithHireLimit(5)));

        Assert.Equal(12, fired.Count);
    }
}
