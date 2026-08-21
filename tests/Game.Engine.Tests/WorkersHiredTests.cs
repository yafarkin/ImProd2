namespace Game.Engine.Tests;

public class WorkersHiredTests
{
    [Fact]
    public void Apply_Adds_Workers_And_Debits_The_Hire_Cost()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        team.Credit(1000m);

        log.Append(new WorkersHired { Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factory.Id, Count = 3, Cost = 150m });

        Assert.Equal(3, factory.Workers);
        Assert.Equal(850m, team.Balance);
    }

    [Fact]
    public void Apply_Allows_The_Hire_Cost_To_Drive_The_Balance_Negative()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);

        log.Append(new WorkersHired { Id = Ulid.NewUlid(), TeamId = team.Id, FactoryId = factory.Id, Count = 2, Cost = 100m });

        Assert.Equal(2, factory.Workers);
        Assert.Equal(-100m, team.Balance); // найм мгновенный — баланс просто уходит в минус, это не ошибка
    }
}
