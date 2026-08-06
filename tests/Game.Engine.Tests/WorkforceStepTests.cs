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
}
