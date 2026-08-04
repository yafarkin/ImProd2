using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Единая раскладка «построенное + что можно построить» на /team (запрос пользователя: один граф
/// вместо двух, плейсхолдер «построить ещё» есть всегда) — над пилотным конфигом (Блок 9.3), тот же
/// приём, что раньше был у <c>FactoryInstanceDiagramTests</c>/<c>FactoryChainDiagramTests</c>
/// (оба слиты в этот класс вместе с самой диаграммой).
/// </summary>
public class FactoryOverviewDiagramTests
{
    private static readonly IReadOnlyDictionary<Ulid, FactoryProfitabilityCalculator.FactoryProfitabilityEstimate> NoEstimates =
        new Dictionary<Ulid, FactoryProfitabilityCalculator.FactoryProfitabilityEstimate>();

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

        var layout = FactoryOverviewDiagram.Build([mine, mill], [], NoEstimates);

        Assert.Equal(2, layout.Nodes.Count);
        Assert.All(layout.Nodes, node => Assert.Null(node.Instance));
        Assert.All(layout.Nodes, node => Assert.Equal(FactoryOverviewDiagram.LoadStatus.NotBuilt, node.Status));
    }

    [Fact]
    public void Build_Keeps_The_Placeholder_Even_When_Instances_Of_The_Type_Already_Exist()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var builtMine = new Factory(Ulid.NewUlid(), sector, mine);

        var layout = FactoryOverviewDiagram.Build([mine, mill], [builtMine], NoEstimates);

        var mineNodes = layout.Nodes.Where(node => node.Definition == mine).ToList();
        Assert.Equal(2, mineNodes.Count); // built instance + placeholder
        Assert.Contains(mineNodes, node => node.Instance == builtMine);
        Assert.Contains(mineNodes, node => node.Instance is null && node.Status == FactoryOverviewDiagram.LoadStatus.NotBuilt);
    }

    [Fact]
    public void Build_Numbers_Instances_Of_The_Same_Type_Starting_From_One()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var firstMine = new Factory(Ulid.NewUlid(), sector, mine);
        var secondMine = new Factory(Ulid.NewUlid(), sector, mine);

        var layout = FactoryOverviewDiagram.Build([mine, mill], [firstMine, secondMine], NoEstimates);

        var indexes = layout.Nodes
            .Where(node => node.Instance is not null)
            .Select(node => node.IndexWithinType)
            .OrderBy(index => index)
            .ToList();
        Assert.Equal([1, 2], indexes);
    }

    [Fact]
    public void Build_Places_The_Placeholder_In_The_Same_Column_As_Built_Instances_Of_Its_Type()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var builtMine = new Factory(Ulid.NewUlid(), sector, mine);

        var layout = FactoryOverviewDiagram.Build([mine, mill], [builtMine], NoEstimates);

        var mineNodes = layout.Nodes.Where(node => node.Definition == mine).ToList();
        Assert.All(mineNodes, node => Assert.Equal(mineNodes[0].X, node.X));
    }

    [Fact]
    public void Build_Marks_Shortage_When_Projected_Output_Falls_Short_Of_Capacity()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var shortInstance = new Factory(Ulid.NewUlid(), sector, mine);
        var estimates = new Dictionary<Ulid, FactoryProfitabilityCalculator.FactoryProfitabilityEstimate>
        {
            [shortInstance.Id] = new(2m, 5m, 20m, 2m, 1m, 17m, HasPriceSignal: true),
        };

        var layout = FactoryOverviewDiagram.Build([mine, mill], [shortInstance], estimates);

        var node = layout.Nodes.Single(n => n.Instance == shortInstance);
        Assert.Equal(FactoryOverviewDiagram.LoadStatus.ShortOfInput, node.Status);
    }

    [Fact]
    public void Build_Marks_Shortage_Even_Without_A_Price_Signal()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var shortInstance = new Factory(Ulid.NewUlid(), sector, mine);
        var estimates = new Dictionary<Ulid, FactoryProfitabilityCalculator.FactoryProfitabilityEstimate>
        {
            [shortInstance.Id] = new(2m, 5m, 0m, 0m, 0m, 0m, HasPriceSignal: false),
        };

        var layout = FactoryOverviewDiagram.Build([mine, mill], [shortInstance], estimates);

        var node = layout.Nodes.Single(n => n.Instance == shortInstance);
        Assert.Equal(FactoryOverviewDiagram.LoadStatus.ShortOfInput, node.Status);
        Assert.Null(node.Profit);
    }

    [Fact]
    public void Build_Defaults_To_Adequate_Without_Raising_A_False_Alarm_When_There_Is_No_Estimate_Yet()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var freshInstance = new Factory(Ulid.NewUlid(), sector, mine);

        var layout = FactoryOverviewDiagram.Build([mine, mill], [freshInstance], NoEstimates);

        var node = layout.Nodes.Single(n => n.Instance == freshInstance);
        Assert.Equal(FactoryOverviewDiagram.LoadStatus.Adequate, node.Status);
        Assert.Null(node.Profit);
    }

    [Fact]
    public void Build_Exposes_Profit_When_Capacity_Is_Met_And_A_Price_Signal_Exists()
    {
        var (mine, mill, sector) = SectorAMineAndMill();
        var healthyInstance = new Factory(Ulid.NewUlid(), sector, mine);
        var estimates = new Dictionary<Ulid, FactoryProfitabilityCalculator.FactoryProfitabilityEstimate>
        {
            [healthyInstance.Id] = new(5m, 5m, 50m, 10m, 5m, 35m, HasPriceSignal: true),
        };

        var layout = FactoryOverviewDiagram.Build([mine, mill], [healthyInstance], estimates);

        var node = layout.Nodes.Single(n => n.Instance == healthyInstance);
        Assert.Equal(FactoryOverviewDiagram.LoadStatus.Adequate, node.Status);
        Assert.Equal(35m, node.Profit);
    }
}
