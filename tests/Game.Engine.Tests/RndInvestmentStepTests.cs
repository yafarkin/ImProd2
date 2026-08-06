using Game.Domain;

namespace Game.Engine.Tests;

public class RndInvestmentStepTests
{
    // Пороги TestGameConfig.Resolved.Raw.Rnd: { 100m, 300m } — 1->2, 2->3.
    private static Factory NewFactory(Team team) => team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);

    [Fact]
    public void Run_Returns_Only_The_Investment_When_The_Threshold_Is_Not_Reached()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team);

        var changes = RndInvestmentStep.Run(team.Id, factory, 50m, TestGameConfig.Resolved.Raw.Rnd);

        var invested = Assert.Single(changes);
        Assert.IsType<RndInvested>(invested);
    }

    [Fact]
    public void Run_Appends_A_Level_Advance_When_The_Investment_Reaches_The_Threshold()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team);

        var changes = RndInvestmentStep.Run(team.Id, factory, 100m, TestGameConfig.Resolved.Raw.Rnd);

        Assert.Equal(2, changes.Count);
        Assert.IsType<RndInvested>(changes[0]);
        var levelAdvanced = Assert.IsType<FactoryLevelAdvanced>(changes[1]);
        Assert.Equal(2, levelAdvanced.NewLevel);
    }

    [Fact]
    public void Run_Appends_One_Level_Advance_Per_Threshold_Crossed_In_A_Single_Investment()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team);

        var changes = RndInvestmentStep.Run(team.Id, factory, 400m, TestGameConfig.Resolved.Raw.Rnd); // покрывает оба порога сразу

        Assert.Equal(3, changes.Count);
        Assert.Equal(2, Assert.IsType<FactoryLevelAdvanced>(changes[1]).NewLevel);
        Assert.Equal(3, Assert.IsType<FactoryLevelAdvanced>(changes[2]).NewLevel);
    }

    [Fact]
    public void Run_Accounts_For_Investment_Already_Made_In_Earlier_Ticks()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team);
        factory.InvestInRnd(80m); // уже вложено раньше, до порога не дотянуло

        var changes = RndInvestmentStep.Run(team.Id, factory, 20m, TestGameConfig.Resolved.Raw.Rnd); // теперь ровно 100 — порог пройден

        Assert.Equal(2, changes.Count);
        Assert.Equal(2, Assert.IsType<FactoryLevelAdvanced>(changes[1]).NewLevel);
    }

    [Fact]
    public void Applying_The_Returned_Changes_End_To_End_Updates_Balance_Investment_And_Level()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team);
        team.Credit(1000m);

        foreach (var change in RndInvestmentStep.Run(team.Id, factory, 100m, TestGameConfig.Resolved.Raw.Rnd))
        {
            log.Append(change);
        }

        Assert.Equal(900m, team.Balance);
        Assert.Equal(100m, factory.RndInvestment);
        Assert.Equal(2, factory.Level);
        Assert.True(log.VerifyIntegrity());
    }

    [Fact]
    public void Run_Does_Nothing_And_Charges_Nothing_Once_The_Factory_Is_Already_At_The_Max_Level()
    {
        // Баг-репорт пользователя: раньше деньги продолжали списываться каждый ход даже после того,
        // как фабрика уже достигла максимального уровня — вкладывать дальше было некуда.
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        var factory = NewFactory(team);
        team.Credit(1000m);
        foreach (var change in RndInvestmentStep.Run(team.Id, factory, 400m, TestGameConfig.Resolved.Raw.Rnd)) // оба порога разом
        {
            log.Append(change);
        }
        Assert.Equal(3, factory.Level); // максимальный уровень при порогах {100, 300}
        var balanceAfterMaxed = team.Balance;

        var changes = RndInvestmentStep.Run(team.Id, factory, 50m, TestGameConfig.Resolved.Raw.Rnd);

        Assert.Empty(changes);
        Assert.Equal(balanceAfterMaxed, team.Balance);
    }
}
