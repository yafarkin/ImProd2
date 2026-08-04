namespace Game.Engine.Tests;

public class FactoryProducedTests
{
    [Fact]
    public void Apply_Removes_Consumed_Inputs_And_Adds_Produced_Output_To_The_Teams_Warehouse()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);
        team.Warehouse.Add(TestGameConfig.Ore, 10m, 0m);

        var change = new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            TeamId = team.Id,
            FactoryId = factory.Id,
            CapacityLimitedOutputQuantity = 3m,
            OutputQuantity = 3m,
            ConsumedInputs = new Dictionary<string, decimal> { [TestGameConfig.Ore.Id] = 6m },
            LaborCost = 15m,
        };

        var entry = log.Append(change);

        Assert.Equal(4m, team.Warehouse.QuantityOf(TestGameConfig.Ore)); // 10 - 6
        Assert.Equal(3m, team.Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.True(log.VerifyIntegrity());
        Assert.Same(change, entry.Change);
    }

    [Fact]
    public void Apply_With_Zero_Output_Leaves_The_Warehouse_Unchanged_But_Still_Records_The_Attempt()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);
        var entriesBefore = log.Entries.Count;

        log.Append(new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            TeamId = team.Id,
            FactoryId = factory.Id,
            CapacityLimitedOutputQuantity = 3m,
            OutputQuantity = 0m,
            ConsumedInputs = new Dictionary<string, decimal> { [TestGameConfig.Ore.Id] = 0m },
            LaborCost = 15m,
        });

        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(entriesBefore + 1, log.Entries.Count); // попытка производства без результата — тоже факт, стоит записи
    }

    [Fact]
    public void Calculate_Then_Apply_End_To_End_Matches_The_Calculator_Result()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);
        factory.Hire(5);
        team.Warehouse.Add(TestGameConfig.Ore, 100m, 0m);

        var result = ProductionCalculator.Calculate(
            factory, team.Warehouse, TestGameConfig.Resolved.Raw.WorkerProductivity, TestGameConfig.Resolved.Raw.Rnd);

        log.Append(new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            TeamId = team.Id,
            FactoryId = result.FactoryId,
            CapacityLimitedOutputQuantity = result.CapacityLimitedOutputQuantity,
            OutputQuantity = result.OutputQuantity,
            ConsumedInputs = result.ConsumedInputs,
            LaborCost = 0m,
        });

        Assert.Equal(100m - result.ConsumedInputs[TestGameConfig.Ore.Id], team.Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Equal(result.OutputQuantity, team.Warehouse.QuantityOf(TestGameConfig.Sheet));
    }

    [Fact]
    public void Apply_Sets_The_Real_Cost_Basis_Of_A_Raw_Material_Factory_To_Just_Its_Labor_Cost()
    {
        // Запрос пользователя: руда добывается «бесплатно», реальная себестоимость — это только
        // зарплата рабочих за ход, а не рыночная цена самой руды.
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);

        log.Append(new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            TeamId = team.Id,
            FactoryId = factory.Id,
            CapacityLimitedOutputQuantity = 5m,
            OutputQuantity = 5m,
            ConsumedInputs = new Dictionary<string, decimal>(),
            LaborCost = 25m,
        });

        Assert.Equal(5m, team.Warehouse.AverageCostOf(TestGameConfig.Ore)); // 25 / 5
    }

    [Fact]
    public void Apply_Cascades_The_Real_Cost_Of_Consumed_Inputs_Into_The_Produced_Output_Instead_Of_Their_Market_Price()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        var mill = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);

        log.Append(new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            TeamId = team.Id,
            FactoryId = mine.Id,
            CapacityLimitedOutputQuantity = 10m,
            OutputQuantity = 10m,
            ConsumedInputs = new Dictionary<string, decimal>(),
            LaborCost = 20m, // реальная себестоимость руды: 2 за единицу
        });

        log.Append(new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            TeamId = team.Id,
            FactoryId = mill.Id,
            CapacityLimitedOutputQuantity = 2m,
            OutputQuantity = 2m,
            ConsumedInputs = new Dictionary<string, decimal> { [TestGameConfig.Ore.Id] = 10m },
            LaborCost = 30m,
        });

        // Себестоимость листов = зарплата завода (30) + реальная (не рыночная) себестоимость
        // потреблённой руды (10 * 2 = 20) = 50, на 2 листа -> 25 за единицу.
        Assert.Equal(0m, team.Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Equal(25m, team.Warehouse.AverageCostOf(TestGameConfig.Sheet));
    }
}
