using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine.Tests;

public class RndInvestmentStepTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Recipe OreMining =
        new("ore-mining", Ore, outputQuantity: 1m, inputs: Array.Empty<RecipeInput>(), productionRate: 1m);
    private static readonly FactoryDefinition Mine = new("iron-mine", "Рудник", SectorA, new[] { OreMining });

    private static readonly RndConfig Config = new()
    {
        CumulativeInvestmentThresholdsByLevel = new[] { 100m, 300m },
        ProductionRateBonusPerLevel = 0.1m,
    };

    private static Team NewTeamWithFactory(out Factory factory)
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        factory = team.BuildFactory(Ulid.NewUlid(), Mine);
        return team;
    }

    [Fact]
    public void Run_Returns_Only_The_Investment_When_The_Threshold_Is_Not_Reached()
    {
        var team = NewTeamWithFactory(out var factory);

        var changes = RndInvestmentStep.Run(factory, 50m, Config);

        var invested = Assert.Single(changes);
        Assert.IsType<RndInvested>(invested);
    }

    [Fact]
    public void Run_Appends_A_Level_Advance_When_The_Investment_Reaches_The_Threshold()
    {
        var team = NewTeamWithFactory(out var factory);

        var changes = RndInvestmentStep.Run(factory, 100m, Config);

        Assert.Equal(2, changes.Count);
        Assert.IsType<RndInvested>(changes[0]);
        var levelAdvanced = Assert.IsType<FactoryLevelAdvanced>(changes[1]);
        Assert.Equal(2, levelAdvanced.NewLevel);
    }

    [Fact]
    public void Run_Appends_One_Level_Advance_Per_Threshold_Crossed_In_A_Single_Investment()
    {
        var team = NewTeamWithFactory(out var factory);

        var changes = RndInvestmentStep.Run(factory, 400m, Config); // покрывает оба порога сразу

        Assert.Equal(3, changes.Count);
        Assert.Equal(2, Assert.IsType<FactoryLevelAdvanced>(changes[1]).NewLevel);
        Assert.Equal(3, Assert.IsType<FactoryLevelAdvanced>(changes[2]).NewLevel);
    }

    [Fact]
    public void Run_Accounts_For_Investment_Already_Made_In_Earlier_Ticks()
    {
        var team = NewTeamWithFactory(out var factory);
        factory.InvestInRnd(80m); // уже вложено раньше, до порога не дотянуло

        var changes = RndInvestmentStep.Run(factory, 20m, Config); // теперь ровно 100 — порог пройден

        Assert.Equal(2, changes.Count);
        Assert.Equal(2, Assert.IsType<FactoryLevelAdvanced>(changes[1]).NewLevel);
    }

    [Fact]
    public void Applying_The_Returned_Changes_End_To_End_Updates_Balance_Investment_And_Level()
    {
        var team = NewTeamWithFactory(out var factory);
        team.Credit(1000m);
        var log = new EventLog<Team>(team);

        foreach (var change in RndInvestmentStep.Run(factory, 100m, Config))
        {
            log.Append(change);
        }

        Assert.Equal(900m, team.Balance);
        Assert.Equal(100m, factory.RndInvestment);
        Assert.Equal(2, factory.Level);
        Assert.True(log.VerifyIntegrity());
    }
}
