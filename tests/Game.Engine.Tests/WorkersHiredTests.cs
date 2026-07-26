using Game.Domain;

namespace Game.Engine.Tests;

public class WorkersHiredTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Recipe OreMining =
        new("ore-mining", Ore, outputQuantity: 1m, inputs: Array.Empty<RecipeInput>(), productionRate: 1m);
    private static readonly FactoryDefinition Mine = new("iron-mine", "Рудник", SectorA, new[] { OreMining });

    [Fact]
    public void Apply_Adds_Workers_And_Debits_The_Hire_Cost()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        var factory = team.BuildFactory(Ulid.NewUlid(), Mine);
        team.Credit(1000m);

        var log = new EventLog<Team>(team);
        log.Append(new WorkersHired { Id = Ulid.NewUlid(), FactoryId = factory.Id, Count = 3, Cost = 150m });

        Assert.Equal(3, factory.Workers);
        Assert.Equal(850m, team.Balance);
    }

    [Fact]
    public void Apply_Allows_The_Hire_Cost_To_Drive_The_Balance_Negative()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        var factory = team.BuildFactory(Ulid.NewUlid(), Mine);

        var log = new EventLog<Team>(team);
        log.Append(new WorkersHired { Id = Ulid.NewUlid(), FactoryId = factory.Id, Count = 2, Cost = 100m });

        Assert.Equal(2, factory.Workers);
        Assert.Equal(-100m, team.Balance); // найм мгновенный — принудительный кредит разберётся с этим на финансовом шаге
    }
}
