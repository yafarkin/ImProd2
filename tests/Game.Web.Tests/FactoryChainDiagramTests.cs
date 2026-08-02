using Game.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>Раскладка цепочки фабрик одного сектора в SVG-координаты (запрос пользователя «список фабрик, а не материалов») — над пилотным конфигом (Блок 9.3).</summary>
public class FactoryChainDiagramTests
{
    private static (IReadOnlyList<FactoryDefinition> SectorDefinitions, string SectorId) SectorAFactoryDefinitions()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        var config = host.DefaultConfig;
        var sectorA = config.Sectors.First();
        var definitions = config.FactoryDefinitions.Where(d => d.Sector.Id == sectorA.Id).ToList();
        return (definitions, sectorA.Id);
    }

    [Fact]
    public void Build_Places_Every_Sector_Factory_Definition_As_A_Node()
    {
        var (definitions, _) = SectorAFactoryDefinitions();

        var layout = FactoryChainDiagram.Build(definitions, Array.Empty<Factory>(), "#2a78d6");

        Assert.Equal(definitions.Count, layout.Nodes.Count);
        Assert.All(layout.Nodes, node => Assert.Empty(node.BuiltInstances));
    }

    [Fact]
    public void Build_Marks_A_Built_Factory_With_Its_Instance_On_The_Node()
    {
        var (definitions, sectorId) = SectorAFactoryDefinitions();
        var mineDefinition = definitions.Single(d => d.Recipes.Single().Output.Level == 0);
        var sector = mineDefinition.Sector;
        var builtMine = new Factory(Ulid.NewUlid(), sector, mineDefinition);

        var layout = FactoryChainDiagram.Build(definitions, new[] { builtMine }, "#2a78d6");

        var mineNode = layout.Nodes.Single(n => n.Definition.Id == mineDefinition.Id);
        Assert.Equal(new[] { builtMine }, mineNode.BuiltInstances);
        var otherNodes = layout.Nodes.Where(n => n.Definition.Id != mineDefinition.Id);
        Assert.All(otherNodes, node => Assert.Empty(node.BuiltInstances));
    }

    [Fact]
    public void Build_Lists_Every_Instance_When_A_Team_Built_Several_Of_The_Same_Type()
    {
        var (definitions, _) = SectorAFactoryDefinitions();
        var mineDefinition = definitions.Single(d => d.Recipes.Single().Output.Level == 0);
        var sector = mineDefinition.Sector;
        var firstMine = new Factory(Ulid.NewUlid(), sector, mineDefinition);
        var secondMine = new Factory(Ulid.NewUlid(), sector, mineDefinition);

        var layout = FactoryChainDiagram.Build(definitions, new[] { firstMine, secondMine }, "#2a78d6");

        var mineNode = layout.Nodes.Single(n => n.Definition.Id == mineDefinition.Id);
        Assert.Equal(2, mineNode.BuiltInstances.Count);
        Assert.Contains(firstMine, mineNode.BuiltInstances);
        Assert.Contains(secondMine, mineNode.BuiltInstances);
        // Один узел на тип, не два — количество экземпляров не размножает узлы на карте.
        Assert.Single(layout.Nodes, n => n.Definition.Id == mineDefinition.Id);
    }

    [Fact]
    public void Build_Draws_An_Edge_Between_A_Producer_And_Its_Consumer_Within_The_Sector()
    {
        var (definitions, _) = SectorAFactoryDefinitions();
        var mineDefinition = definitions.Single(d => d.Recipes.Single().Output.Level == 0);
        var millDefinition = definitions.Single(d => d.Recipes.Single().Inputs.Any(i => i.Material == mineDefinition.Recipes[0].Output));

        var layout = FactoryChainDiagram.Build(definitions, Array.Empty<Factory>(), "#2a78d6");

        var mineNode = layout.Nodes.Single(n => n.Definition.Id == mineDefinition.Id);
        var millNode = layout.Nodes.Single(n => n.Definition.Id == millDefinition.Id);
        Assert.Contains(layout.Edges, e => e.X1 == mineNode.X + mineNode.Width && e.X2 == millNode.X);
        Assert.True(millNode.X > mineNode.X);
    }
}
