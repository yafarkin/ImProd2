namespace Game.Engine.Tests;

/// <summary>
/// Автоматическое приведение штата фабрики к объявленной численности (<see
/// cref="Domain.Factory.DesiredWorkers"/>) внутри <see cref="TickFinanceStep"/> — один раз за ход, по
/// итоговой разнице, а не при каждом промежуточном объявлении (запрос пользователя, см. doc-comment
/// TickFinanceStep.Run). Сама логика найма/увольнения не меняется — переиспользуется <see
/// cref="WorkforceStep"/>, отдельно проверенный в WorkforceStepTests. Объявление (<see
/// cref="GameSession.SetWorkerCount"/>) — отдельно, в GameSessionFactoryTests.
/// </summary>
public class TickFinanceStepWorkforceTests
{
    private static readonly Config.Session.StartingConditionsConfig LoanConfig = TestGameConfig.Resolved.Raw.StartingConditions;
    private static readonly Config.Economy.WorkerProductivityConfig WorkerConfig = TestGameConfig.Resolved.Raw.WorkerProductivity;
    private static readonly Config.Economy.WarehouseConfig WarehouseConfig = TestGameConfig.Resolved.Raw.Warehouse;
    private static readonly IReadOnlyList<Config.Catalog.FactoryDefinitionConfig> FactoryDefinitions = TestGameConfig.Resolved.Raw.FactoryDefinitions;
    private static readonly Config.Economy.RndConfig RndConfig = TestGameConfig.Resolved.Raw.Rnd;
    private static readonly Config.Economy.GenerationResearchConfig GenerationResearchConfig = TestGameConfig.Resolved.Raw.GenerationResearch;
    private static readonly Config.Economy.WearConfig WearConfig = TestGameConfig.Resolved.Raw.Wear;

    [Fact]
    public void Run_Charges_Hiring_Once_Before_Salaries_Regardless_Of_How_Many_Times_It_Was_Redeclared()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetDesiredWorkers(10); // команда меняла решение несколько раз за ход...
        factory.SetDesiredWorkers(3);
        factory.SetDesiredWorkers(5); // ...в итоге остановилась на 5
        team.Credit(1000m);

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m, wearConfig: WearConfig, currentTurn: 1);

        Assert.Equal(2, changes.Count);
        var hired = Assert.IsType<WorkersHired>(changes[0]);
        Assert.Equal(5, hired.Count); // одно списание, по итоговой разнице, а не 10+3+5
        Assert.Equal(5 * 50m, hired.Cost);
        var salaries = Assert.IsType<SalariesPaid>(changes[1]);
        Assert.Equal(5, salaries.TotalWorkers); // зарплата уже по новой, объявленной численности
    }

    [Fact]
    public void Run_Charges_Nothing_When_The_Desired_Count_Matches_The_Current_One()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // наём и объявление синхронизируются сами (см. doc-comment Factory.Hire)

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m, wearConfig: WearConfig, currentTurn: 1);

        Assert.DoesNotContain(changes, c => c is WorkersHired or WorkersFired);
    }

    [Fact]
    public void An_Unaffordable_Hire_Is_Still_Charged_In_Full()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetDesiredWorkers(5); // баланс пуст — платить нечем

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m, wearConfig: WearConfig, currentTurn: 1);
        foreach (var change in changes)
        {
            log.Append(change);
        }

        var hired = Assert.IsType<WorkersHired>(changes[0]);
        Assert.Equal(5 * 50m, hired.Cost); // не урезается из-за нехватки баланса
        // Принудительный заём, который раньше покрывал эту дыру здесь же, теперь отдельный, самый
        // последний шаг всего тика (ForcedLoanStep, см. doc-comment TickFinanceStep и ForcedLoanStepTests).
        Assert.Equal(-(5 * 50m + 5 * 5m), team.Balance); // наём (250) + зарплата (25) за те же 5 голов
    }
}
