namespace Game.Engine.Tests;

/// <summary>
/// Автоматическое списание R&amp;D-обязательства фабрики (<see cref="Factory.RndCommitmentPerTurn"/>)
/// внутри <see cref="TickFinanceStep"/> — постоянные затраты за ход, а не разовое вложение (запрос
/// пользователя, см. doc-comment TickFinanceStep.Run). Сама логика вложения/перехода уровня не
/// меняется — переиспользуется <see cref="RndInvestmentStep"/>, отдельно проверенный в
/// RndInvestmentStepTests. Объявление суммы (<see cref="GameSession.SetRndCommitment"/>) — отдельно,
/// в GameSessionRndInvestmentTests.
/// </summary>
public class TickFinanceStepRndTests
{
    // TestGameConfig.Resolved.Raw.Rnd: пороги { 100m, 300m } — 1->2, 2->3; MaxCommitmentPerTurn=200m.
    private static readonly Config.Session.StartingConditionsConfig LoanConfig = TestGameConfig.Resolved.Raw.StartingConditions;
    private static readonly Config.Economy.WorkerProductivityConfig WorkerConfig = TestGameConfig.Resolved.Raw.WorkerProductivity;
    private static readonly Config.Economy.WarehouseConfig WarehouseConfig = TestGameConfig.Resolved.Raw.Warehouse;
    private static readonly IReadOnlyList<Config.Catalog.FactoryDefinitionConfig> FactoryDefinitions = TestGameConfig.Resolved.Raw.FactoryDefinitions;
    private static readonly Config.Economy.RndConfig RndConfig = TestGameConfig.Resolved.Raw.Rnd;
    private static readonly Config.Economy.GenerationResearchConfig GenerationResearchConfig = TestGameConfig.Resolved.Raw.GenerationResearch;

    [Fact]
    public void Run_Charges_Nothing_For_A_Factory_With_No_Rnd_Commitment()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine); // RndCommitmentPerTurn по умолчанию 0

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Charges_The_Committed_Amount_And_Accumulates_Investment_Below_The_Threshold()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetRndCommitment(50m);
        team.Credit(100m); // с запасом

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        var invested = Assert.IsType<RndInvested>(Assert.Single(changes));
        Assert.Equal(50m, invested.Amount);
        Assert.Equal(factory.Id, invested.FactoryId);
    }

    [Fact]
    public void Run_Appends_FactoryLevelAdvanced_When_The_Committed_Amount_Crosses_A_Threshold()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetRndCommitment(100m); // ровно первый порог
        team.Credit(200m);

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Equal(2, changes.Count);
        Assert.IsType<RndInvested>(changes[0]);
        var levelAdvanced = Assert.IsType<FactoryLevelAdvanced>(changes[1]);
        Assert.Equal(2, levelAdvanced.NewLevel);
    }

    [Fact]
    public void Run_Charges_Rnd_Between_Factory_Upkeep_And_The_Warehouse_Fee()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetRndCommitment(50m);
        team.Warehouse.Add(TestGameConfig.Ore, 1005m, 0m); // сверх лимита (1000) на 5 единиц
        team.Credit(1000m); // с запасом

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Equal(2, changes.Count);
        Assert.IsType<RndInvested>(changes[0]);
        Assert.IsType<WarehouseFeeCharged>(changes[1]);
    }

    [Fact]
    public void An_Unaffordable_Rnd_Commitment_Is_Still_Charged_In_Full()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetRndCommitment(50m); // баланс пуст — платить нечем

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);
        foreach (var change in changes)
        {
            log.Append(change);
        }

        var invested = Assert.IsType<RndInvested>(changes[0]);
        Assert.Equal(50m, invested.Amount); // вложение не урезается из-за нехватки баланса
        // Принудительный заём, который раньше покрывал эту дыру здесь же, теперь отдельный, самый
        // последний шаг всего тика (ForcedLoanStep, см. doc-comment TickFinanceStep и ForcedLoanStepTests).
        Assert.Equal(-50m, team.Balance);
    }

    [Fact]
    public void Run_Charges_Nothing_Once_The_Factory_Is_Already_At_The_Max_Level_Even_With_A_Positive_Commitment()
    {
        // Баг-репорт пользователя: команда объявила R&D-обязательство, фабрика вкачалась до
        // максимального уровня, но команда не сняла объявление — деньги не должны продолжать списываться.
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.AdvanceLevel(); // 1 -> 2
        factory.AdvanceLevel(); // 2 -> 3, максимальный уровень при порогах {100, 300}
        factory.SetRndCommitment(50m);
        team.Credit(1000m);

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.DoesNotContain(changes, c => c is RndInvested or FactoryLevelAdvanced);
    }
}
