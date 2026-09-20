namespace Game.Engine.Tests;

public class MoneyEventsTests
{
    [Fact]
    public void SalariesPaid_Debits_The_Balance()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.Credit(1000m);

        log.Append(new SalariesPaid { Id = Ulid.NewUlid(), TeamId = team.Id, TotalWorkers = 7, Amount = 35m });

        Assert.Equal(965m, team.Balance);
    }
}
