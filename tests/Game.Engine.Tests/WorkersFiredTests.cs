using Game.Domain;

namespace Game.Engine.Tests;

public class WorkersFiredTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Recipe OreMining =
        new("ore-mining", Ore, outputQuantity: 1m, inputs: Array.Empty<RecipeInput>(), productionRate: 1m);
    private static readonly FactoryDefinition Mine = new("iron-mine", "Рудник", SectorA, new[] { OreMining });

    [Fact]
    public void Apply_Removes_Workers_And_Debits_The_Fire_Cost()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        var factory = team.BuildFactory(Ulid.NewUlid(), Mine);
        factory.Hire(5);
        team.Credit(1000m);

        var log = new EventLog<Team>(team);
        log.Append(new WorkersFired { Id = Ulid.NewUlid(), FactoryId = factory.Id, Count = 2, Cost = 60m });

        Assert.Equal(3, factory.Workers);
        Assert.Equal(940m, team.Balance);
    }

    [Fact]
    public void Apply_Throws_And_Does_Not_Debit_When_Firing_More_Than_Available()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        var factory = team.BuildFactory(Ulid.NewUlid(), Mine);
        factory.Hire(1);
        team.Credit(1000m);

        var log = new EventLog<Team>(team);

        Assert.Throws<InvalidOperationException>(() =>
            log.Append(new WorkersFired { Id = Ulid.NewUlid(), FactoryId = factory.Id, Count = 2, Cost = 60m }));

        Assert.Equal(1, factory.Workers);
        Assert.Equal(1000m, team.Balance); // Factory.Fire бросает раньше, чем событие попадёт в журнал — Debit не вызван
        Assert.Empty(log.Entries);
    }
}
