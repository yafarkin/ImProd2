namespace Game.Engine.Tests;

public class WorkersFiredTests
{
    [Fact]
    public void Apply_Removes_Workers_And_Debits_The_Fire_Cost()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(5);
        team.Credit(1000m);

        log.Append(new WorkersFired { Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factory.Id, Count = 2, Cost = 60m });

        Assert.Equal(3, factory.Workers);
        Assert.Equal(940m, team.Balance);
    }

    [Fact]
    public void Apply_Throws_And_Does_Not_Debit_When_Firing_More_Than_Available()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(1);
        team.Credit(1000m);
        var entriesBefore = log.Entries.Count;

        Assert.Throws<InvalidOperationException>(() =>
            log.Append(new WorkersFired { Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factory.Id, Count = 2, Cost = 60m }));

        Assert.Equal(1, factory.Workers);
        Assert.Equal(1000m, team.Balance); // Factory.Fire бросает раньше, чем событие попадёт в журнал — Debit не вызван
        Assert.Equal(entriesBefore, log.Entries.Count);
    }
}
