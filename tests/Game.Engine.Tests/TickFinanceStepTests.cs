namespace Game.Engine.Tests;

public class TickFinanceStepTests
{
    private static readonly Config.Economy.WorkerProductivityConfig WorkerConfig = TestGameConfig.Resolved.Raw.WorkerProductivity;
    // TestGameConfig: FreeCapacity=1000 (намного больше остатков в этих тестах) — плата за склад не начисляется по умолчанию.
    private static readonly Config.Economy.WarehouseConfig WarehouseConfig = TestGameConfig.Resolved.Raw.Warehouse;
    // TestGameConfig.Mine/SteelMill: FixedCostPerTurn=0 — эти тесты про остальные шаги, отдельные
    // тесты на капитальные затраты — в FactoryUpkeepTests.
    private static readonly IReadOnlyList<Config.Catalog.FactoryDefinitionConfig> FactoryDefinitions = TestGameConfig.Resolved.Raw.FactoryDefinitions;
    // Ни у одной фабрики этих тестов нет RndCommitmentPerTurn (по умолчанию 0) — эти тесты не про
    // R&D, отдельные тесты на него — в TickFinanceStepRndTests.
    private static readonly Config.Economy.RndConfig RndConfig = TestGameConfig.Resolved.Raw.Rnd;
    // Ни у одной команды этих тестов нет GenerationResearchCommitmentPerTurn (по умолчанию 0) — эти
    // тесты не про исследование поколений, отдельные тесты на него — в TickFinanceStepGenerationResearchTests.
    private static readonly Config.Economy.GenerationResearchConfig GenerationResearchConfig = TestGameConfig.Resolved.Raw.GenerationResearch;
    private static readonly Config.Economy.WearConfig WearConfig = TestGameConfig.Resolved.Raw.Wear;

    [Fact]
    public void Run_Returns_Nothing_For_A_Team_With_No_Workers_And_No_Costs()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();

        var changes = TickFinanceStep.Run(team, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, wearConfig: WearConfig, currentTurn: 1);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Charges_Salaries_For_Hired_Workers()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Credit(1000m); // с запасом
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 4 * 5 = 20

        var changes = TickFinanceStep.Run(team, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, wearConfig: WearConfig, currentTurn: 1);

        var salaries = Assert.IsType<SalariesPaid>(Assert.Single(changes));
        Assert.Equal(20m, salaries.Amount);
        Assert.Equal(4, salaries.TotalWorkers);
    }

    [Fact]
    public void Run_Charges_Salaries_In_Full_Even_When_The_Balance_Cannot_Cover_Them()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Credit(10m); // не хватит на зарплату (20)
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 20

        var changes = TickFinanceStep.Run(team, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, wearConfig: WearConfig, currentTurn: 1);

        var salaries = Assert.IsType<SalariesPaid>(Assert.Single(changes));
        Assert.Equal(20m, salaries.Amount); // не урезано из-за нехватки баланса — баланс уходит в минус, это ожидаемо
    }

    [Fact]
    public void Run_Charges_A_Warehouse_Fee_Last_After_Salaries()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Credit(1000m); // с запасом
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 20
        team.Warehouse.Add(TestGameConfig.Ore, 15m, 0m); // сверх лимита (10) на 5 единиц
        var warehouseConfig = new Config.Economy.WarehouseConfig { FreeCapacity = 10m, OverageFeePerUnit = 2m };

        var changes = TickFinanceStep.Run(team, WorkerConfig, warehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, wearConfig: WearConfig, currentTurn: 1);

        Assert.Equal(2, changes.Count);
        Assert.IsType<SalariesPaid>(changes[0]);
        var fee = Assert.IsType<WarehouseFeeCharged>(changes[1]);
        Assert.Equal(5m, fee.OverageQuantity);
        Assert.Equal(10m, fee.Amount); // 5 * 2
    }

    [Fact]
    public void Run_Charges_An_Unpayable_Warehouse_Fee_In_Full()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Credit(20m); // хватит на зарплату (20), но не на плату за склад
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 20
        team.Warehouse.Add(TestGameConfig.Ore, 15m, 0m); // сверх лимита (10) на 5 единиц
        var warehouseConfig = new Config.Economy.WarehouseConfig { FreeCapacity = 10m, OverageFeePerUnit = 5m }; // плата = 25

        var changes = TickFinanceStep.Run(team, WorkerConfig, warehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, wearConfig: WearConfig, currentTurn: 1);

        Assert.Equal(2, changes.Count);
        var fee = Assert.IsType<WarehouseFeeCharged>(changes[1]);
        Assert.Equal(25m, fee.Amount); // не урезано из-за нехватки баланса; баланс после: 20-20-25 = -25
    }

    [Fact]
    public void Run_Charges_No_Fee_When_Stock_Is_Within_Free_Capacity()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Warehouse.Add(TestGameConfig.Ore, 5m, 0m); // намного меньше лимита по умолчанию (1000)

        var changes = TickFinanceStep.Run(team, WorkerConfig, WarehouseConfig, FactoryDefinitions, RndConfig, GenerationResearchConfig, wearConfig: WearConfig, currentTurn: 1);

        Assert.Empty(changes);
    }
}
