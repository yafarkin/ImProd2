namespace Game.Engine.Tests;

/// <summary>
/// Автоматическое списание командного обязательства по исследованию следующего поколения
/// (<see cref="Team.GenerationResearchCommitmentPerTurn"/>) внутри <see cref="TickFinanceStep"/> —
/// постоянные затраты за ход, тем же приёмом, что <see cref="TickFinanceStepRndTests"/> для одной
/// фабрики, только на уровне команды. Сама логика вложения/перехода поколения не меняется —
/// переиспользуется <see cref="GenerationResearchStep"/>. Объявление суммы
/// (<see cref="GameSession.SetGenerationResearchCommitment"/>) — отдельно, в
/// GameSessionGenerationResearchTests.
/// </summary>
public class TickFinanceStepGenerationResearchTests
{
    private static readonly Config.Session.StartingConditionsConfig LoanConfig = TestGameConfig.Resolved.Raw.StartingConditions;
    private static readonly Config.Economy.WorkerProductivityConfig WorkerConfig = TestGameConfig.Resolved.Raw.WorkerProductivity;
    private static readonly Config.Economy.WarehouseConfig WarehouseConfig = TestGameConfig.Resolved.Raw.Warehouse;
    private static readonly IReadOnlyList<Config.Catalog.FactoryDefinitionConfig> FactoryDefinitions = TestGameConfig.Resolved.Raw.FactoryDefinitions;
    private static readonly Config.Economy.RndConfig RndConfig = TestGameConfig.Resolved.Raw.Rnd;

    // Пороги в очках исследований: 100^0.5=10, 400^0.5=20 — накопленные ¤ 100 и 400 (1->2, 2->3).
    private static readonly Config.Economy.GenerationResearchConfig GenerationResearchConfig = new()
    {
        StartingGeneration = 1,
        ResearchPointThresholdsByGeneration = new[] { 10m, 20m },
        DiminishingReturnsExponent = 0.5m,
        MaxCommitmentPerTurn = 300m,
    };

    [Fact]
    public void Run_Charges_Nothing_For_A_Team_With_No_Generation_Research_Commitment()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam(); // GenerationResearchCommitmentPerTurn по умолчанию 0

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Charges_The_Committed_Amount_And_Accumulates_Investment_Below_The_Threshold()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.SetGenerationResearchCommitment(50m);
        team.Credit(100m); // с запасом

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        var invested = Assert.IsType<GenerationResearchInvested>(Assert.Single(changes));
        Assert.Equal(50m, invested.Amount);
        Assert.Equal(team.Id, invested.TeamId);
    }

    [Fact]
    public void Run_Appends_TeamGenerationAdvanced_When_The_Committed_Amount_Crosses_A_Threshold()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.SetGenerationResearchCommitment(100m); // ровно первый порог по накопленным ¤
        team.Credit(200m);

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Equal(2, changes.Count);
        Assert.IsType<GenerationResearchInvested>(changes[0]);
        var advanced = Assert.IsType<TeamGenerationAdvanced>(changes[1]);
        Assert.Equal(2, advanced.NewGeneration);
    }

    [Fact]
    public void Run_Charges_Generation_Research_Between_Factory_Rnd_And_The_Warehouse_Fee()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.SetRndCommitment(20m);
        team.SetGenerationResearchCommitment(50m);
        team.Warehouse.Add(TestGameConfig.Ore, 1005m, 0m); // сверх лимита (1000) на 5 единиц
        team.Credit(1000m); // с запасом

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.Equal(3, changes.Count);
        Assert.IsType<RndInvested>(changes[0]);
        Assert.IsType<GenerationResearchInvested>(changes[1]);
        Assert.IsType<WarehouseFeeCharged>(changes[2]);
    }

    [Fact]
    public void An_Unaffordable_Generation_Research_Commitment_Is_Still_Charged_In_Full()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.SetGenerationResearchCommitment(50m); // баланс пуст — платить нечем

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);
        foreach (var change in changes)
        {
            log.Append(change);
        }

        var invested = Assert.IsType<GenerationResearchInvested>(changes[0]);
        Assert.Equal(50m, invested.Amount); // вложение не урезается из-за нехватки баланса
        // Принудительный заём, который раньше покрывал эту дыру здесь же, теперь отдельный, самый
        // последний шаг всего тика (ForcedLoanStep, см. doc-comment TickFinanceStep и ForcedLoanStepTests).
        Assert.Equal(-50m, team.Balance);
    }

    [Fact]
    public void Run_Charges_Nothing_Once_The_Team_Is_Already_At_The_Max_Generation_Even_With_A_Positive_Commitment()
    {
        // Баг-репорт пользователя: команда объявила обязательство по исследованию поколения,
        // разблокировала максимальное поколение, но не сняла объявление — деньги не должны
        // продолжать списываться.
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.AdvanceGeneration(); // 1 -> 2
        team.AdvanceGeneration(); // 2 -> 3, максимальное поколение при порогах {10, 20}
        team.SetGenerationResearchCommitment(50m);
        team.Credit(1000m);

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, reputationPercentage: 100m);

        Assert.DoesNotContain(changes, c => c is GenerationResearchInvested or TeamGenerationAdvanced);
    }
}
