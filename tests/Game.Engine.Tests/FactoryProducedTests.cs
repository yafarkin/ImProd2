using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine.Tests;

public class FactoryProducedTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    private static readonly Recipe SheetRecipe =
        new("sheet-from-ore", Sheet, outputQuantity: 1m, inputs: new[] { new RecipeInput(Ore, 2m) }, productionRate: 1m);

    private static readonly FactoryDefinition Mill =
        new("steel-mill", "Сталелитейный завод", SectorA, new[] { SheetRecipe });

    [Fact]
    public void Apply_Removes_Consumed_Inputs_And_Adds_Produced_Output_To_The_Teams_Warehouse()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А", SectorA);
        var factory = team.BuildFactory(Ulid.NewUlid(), Mill);
        team.Warehouse.Add(Ore, 10m);

        var log = new EventLog<Team>(team);
        var change = new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            FactoryId = factory.Id,
            CapacityLimitedOutputQuantity = 3m,
            OutputQuantity = 3m,
            ConsumedInputs = new Dictionary<string, decimal> { [Ore.Id] = 6m },
        };

        var entry = log.Append(change);

        Assert.Equal(4m, team.Warehouse.QuantityOf(Ore)); // 10 - 6
        Assert.Equal(3m, team.Warehouse.QuantityOf(Sheet));
        Assert.True(log.VerifyIntegrity());
        Assert.Same(change, entry.Change);
    }

    [Fact]
    public void Apply_With_Zero_Output_Leaves_The_Warehouse_Unchanged_But_Still_Records_The_Attempt()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А", SectorA);
        var factory = team.BuildFactory(Ulid.NewUlid(), Mill);

        var log = new EventLog<Team>(team);
        log.Append(new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            FactoryId = factory.Id,
            CapacityLimitedOutputQuantity = 3m,
            OutputQuantity = 0m,
            ConsumedInputs = new Dictionary<string, decimal> { [Ore.Id] = 0m },
        });

        Assert.Equal(0m, team.Warehouse.QuantityOf(Ore));
        Assert.Equal(0m, team.Warehouse.QuantityOf(Sheet));
        Assert.Single(log.Entries); // попытка производства без результата — тоже факт, стоит записи
    }

    [Fact]
    public void Calculate_Then_Apply_End_To_End_Matches_The_Calculator_Result()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А", SectorA);
        var factory = team.BuildFactory(Ulid.NewUlid(), Mill);
        factory.Hire(5);
        team.Warehouse.Add(Ore, 100m);

        var productivity = new WorkerProductivityConfig
        {
            BaseWorkerCount = 5,
            DiminishingReturnsFactor = 0.5m,
            HireCostPerWorker = 100m,
            FireCostPerWorker = 50m,
        };
        var result = ProductionCalculator.Calculate(factory, team.Warehouse, productivity);

        var log = new EventLog<Team>(team);
        log.Append(new FactoryProduced
        {
            Id = Ulid.NewUlid(),
            FactoryId = result.FactoryId,
            CapacityLimitedOutputQuantity = result.CapacityLimitedOutputQuantity,
            OutputQuantity = result.OutputQuantity,
            ConsumedInputs = result.ConsumedInputs,
        });

        Assert.Equal(100m - result.ConsumedInputs[Ore.Id], team.Warehouse.QuantityOf(Ore));
        Assert.Equal(result.OutputQuantity, team.Warehouse.QuantityOf(Sheet));
    }
}
