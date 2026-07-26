namespace Game.Engine.Tests;

public class FactoryProducedTests
{
    [Fact]
    public void Apply_Removes_Consumed_Inputs_And_Adds_Produced_Output_To_The_Teams_Warehouse()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill);
        team.Warehouse.Add(TestGameConfig.Ore, 10m);

        var change = new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            TeamId = team.Id,
            FactoryId = factory.Id,
            CapacityLimitedOutputQuantity = 3m,
            OutputQuantity = 3m,
            ConsumedInputs = new Dictionary<string, decimal> { [TestGameConfig.Ore.Id] = 6m },
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
        team.Warehouse.Add(TestGameConfig.Ore, 100m);

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
        });

        Assert.Equal(100m - result.ConsumedInputs[TestGameConfig.Ore.Id], team.Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Equal(result.OutputQuantity, team.Warehouse.QuantityOf(TestGameConfig.Sheet));
    }
}
