using Game.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Единый список «построенное + что можно построить» на /team (запрос пользователя: один список
/// вместо двух, плейсхолдер «построить ещё» есть всегда; позже — вертикальный список вместо
/// горизонтальной SVG-диаграммы и реальный факт выпуска вместо рыночной оценки прибыли, см. doc-comment
/// <see cref="FactoryOverviewList"/>).
/// </summary>
public class FactoryOverviewListTests
{
    private static readonly IReadOnlyDictionary<Ulid, decimal> NoOutputs = new Dictionary<Ulid, decimal>();
    private static readonly IReadOnlyDictionary<Ulid, decimal> NoMaxes = new Dictionary<Ulid, decimal>();

    private static (FactoryDefinition Mine, FactoryDefinition Mill, Sector Sector) SectorAMineAndMill()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        var config = host.DefaultConfig;
        var sectorA = config.Sectors.First();
        var definitions = config.FactoryDefinitions.Where(d => d.Sector.Id == sectorA.Id).ToList();
        var mine = definitions.Single(d => d.Recipes.Single().Output.Level == 0);
        var mill = definitions.Single(d => d.Recipes.Single().Inputs.Any(i => i.Material == mine.Recipes[0].Output));
        return (mine, mill, mine.Sector);
    }

    [Fact]
    public void Build_Adds_A_Buildable_Placeholder_For_Every_Type_Even_With_Nothing_Built()
    {
        var (mine, mill, _) = SectorAMineAndMill();

        var nodes = FactoryOverviewList.Build([mine, mill], [], NoOutputs, NoMaxes);

        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, node => Assert.Null(node.Instance));
        Assert.All(nodes, node => Assert.Equal(FactoryOverviewList.LoadStatus.NotBuilt, node.Status));
    }

    [Fact]
    public void Build_Keeps_The_Placeholder_Even_When_Instances_Of_The_Type_Already_Exist()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var builtMine = new Factory(Ulid.NewUlid(), sector, mine);

        var nodes = FactoryOverviewList.Build([mine, mill], [builtMine], NoOutputs, NoMaxes);

        var mineNodes = nodes.Where(node => node.Definition == mine).ToList();
        Assert.Equal(2, mineNodes.Count); // built instance + placeholder
        Assert.Contains(mineNodes, node => node.Instance == builtMine);
        Assert.Contains(mineNodes, node => node.Instance is null && node.Status == FactoryOverviewList.LoadStatus.NotBuilt);
    }

    [Fact]
    public void Build_Numbers_Instances_Of_The_Same_Type_Starting_From_One()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var firstMine = new Factory(Ulid.NewUlid(), sector, mine);
        var secondMine = new Factory(Ulid.NewUlid(), sector, mine);

        var nodes = FactoryOverviewList.Build([mine, mill], [firstMine, secondMine], NoOutputs, NoMaxes);

        var indexes = nodes
            .Where(node => node.Instance is not null)
            .Select(node => node.IndexWithinType)
            .OrderBy(index => index)
            .ToList();
        Assert.Equal([1, 2], indexes);
    }

    [Fact]
    public void Build_Groups_The_Placeholder_With_Built_Instances_Of_Its_Type()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var builtMine = new Factory(Ulid.NewUlid(), sector, mine);

        var nodes = FactoryOverviewList.Build([mine, mill], [builtMine], NoOutputs, NoMaxes);

        var mineIndexes = nodes.Select((node, index) => (node, index)).Where(pair => pair.node.Definition == mine).Select(pair => pair.index).ToList();
        // Экземпляр и плейсхолдер того же типа должны идти подряд, не вперемешку с другим типом.
        Assert.Equal(mineIndexes.Order(), mineIndexes);
        Assert.Equal(2, mineIndexes.Count);
        Assert.Equal(mineIndexes[1], mineIndexes[0] + 1);
    }

    [Fact]
    public void Build_Marks_Shortage_When_The_Last_Turns_Output_Falls_Short_Of_The_Theoretical_Max()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var shortInstance = new Factory(Ulid.NewUlid(), sector, mine);
        var outputs = new Dictionary<Ulid, decimal> { [shortInstance.Id] = 2m };
        var maxes = new Dictionary<Ulid, decimal> { [shortInstance.Id] = 5m };

        var nodes = FactoryOverviewList.Build([mine, mill], [shortInstance], outputs, maxes);

        var node = nodes.Single(n => n.Instance == shortInstance);
        Assert.Equal(FactoryOverviewList.LoadStatus.ShortOfInput, node.Status);
        Assert.Equal(2m, node.LastTurnOutput);
        Assert.Equal(5m, node.TheoreticalMaxOutput);
    }

    [Fact]
    public void Build_Defaults_To_Adequate_Without_Raising_A_False_Alarm_When_There_Is_No_Turn_History_Yet()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var freshInstance = new Factory(Ulid.NewUlid(), sector, mine);

        var nodes = FactoryOverviewList.Build([mine, mill], [freshInstance], NoOutputs, NoMaxes);

        var node = nodes.Single(n => n.Instance == freshInstance);
        Assert.Equal(FactoryOverviewList.LoadStatus.Adequate, node.Status);
        Assert.Null(node.LastTurnOutput);
    }

    [Fact]
    public void Build_Is_Adequate_When_The_Last_Turns_Output_Reaches_The_Theoretical_Max()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var healthyInstance = new Factory(Ulid.NewUlid(), sector, mine);
        var outputs = new Dictionary<Ulid, decimal> { [healthyInstance.Id] = 5m };
        var maxes = new Dictionary<Ulid, decimal> { [healthyInstance.Id] = 5m };

        var nodes = FactoryOverviewList.Build([mine, mill], [healthyInstance], outputs, maxes);

        var node = nodes.Single(n => n.Instance == healthyInstance);
        Assert.Equal(FactoryOverviewList.LoadStatus.Adequate, node.Status);
        Assert.Equal(5m, node.LastTurnOutput);
    }
}
