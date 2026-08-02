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
        Assert.All(layout.Nodes, node => Assert.Null(node.Built));
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
        Assert.Same(builtMine, mineNode.Built);
        var otherNodes = layout.Nodes.Where(n => n.Definition.Id != mineDefinition.Id);
        Assert.All(otherNodes, node => Assert.Null(node.Built));
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

    [Fact]
    public void Build_Uses_The_Built_Factorys_Selected_Recipe_For_Its_Position_And_Label()
    {
        var (definitions, _) = SectorAFactoryDefinitions();
        var mineDefinition = definitions.Single(d => d.Recipes.Single().Output.Level == 0);
        var sector = mineDefinition.Sector;
        var builtMine = new Factory(Ulid.NewUlid(), sector, mineDefinition);

        var layout = FactoryChainDiagram.Build(definitions, new[] { builtMine }, "#2a78d6");

        var mineNode = layout.Nodes.Single(n => n.Definition.Id == mineDefinition.Id);
        Assert.Equal(builtMine.SelectedRecipe, mineNode.Recipe);
    }
}
