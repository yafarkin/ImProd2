using Game.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>Раскладка цепочки материалов в SVG-координаты (запрос пользователя «отрисовка всей цепочки материалов») — над пилотным конфигом (Блок 9.3).</summary>
public class MaterialChainDiagramTests
{
    private static Game.Config.Loading.ResolvedGameConfig PilotConfig()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        return host.DefaultConfig;
    }

    [Fact]
    public void Build_Places_Every_Material_As_A_Node_And_Every_Recipe_Input_As_An_Edge()
    {
        var config = PilotConfig();

        var layout = MaterialChainDiagram.Build(config);

        Assert.Equal(config.Materials.Count, layout.Nodes.Count);
        var expectedEdgeCount = config.Materials.Values
            .Select(material => config.RecipeBook.TryGetRecipe(material))
            .Where(recipe => recipe is not null)
            .Sum(recipe => recipe!.Inputs.Count);
        Assert.Equal(expectedEdgeCount, layout.Edges.Count);
    }

    [Fact]
    public void Build_Colors_Materials_Of_The_Same_Sector_Alike_And_Different_Sectors_Differently()
    {
        var config = PilotConfig();
        var layout = MaterialChainDiagram.Build(config);

        var bySector = layout.Nodes.ToLookup(node => node.Material.Sector.Id);
        Assert.True(bySector.Count >= 2, "Pilot config is expected to have at least two sectors.");

        foreach (var sectorNodes in bySector)
        {
            Assert.Single(sectorNodes.Select(node => node.Color).Distinct());
        }

        var colorsPerSector = bySector.Select(group => group.First().Color).ToList();
        Assert.Equal(colorsPerSector.Count, colorsPerSector.Distinct().Count());
    }

    [Fact]
    public void Build_Places_Raw_Materials_In_The_Leftmost_Column_And_Higher_Levels_Further_Right()
    {
        var config = PilotConfig();
        var layout = MaterialChainDiagram.Build(config);

        var rawX = layout.Nodes.Where(node => node.Material.IsRawMaterial).Select(node => node.X).Distinct().Single();
        foreach (var node in layout.Nodes.Where(node => !node.Material.IsRawMaterial))
        {
            Assert.True(node.X > rawX);
        }
    }

    [Fact]
    public void Build_Labels_Edges_With_The_Recipe_Input_Ratio_Per_One_Unit_Of_Output()
    {
        var config = PilotConfig();
        var sheet = config.Materials["sheet"];
        var recipe = config.RecipeBook.GetRecipe(sheet);
        var oreInput = recipe.Inputs.Single();

        var layout = MaterialChainDiagram.Build(config);

        var expectedLabel = "×" + (oreInput.Quantity / recipe.OutputQuantity).ToString("0.##");
        var sheetNode = layout.Nodes.Single(n => n.Material.Id == "sheet");
        var targetY = sheetNode.Y + sheetNode.Height / 2;
        // "sheet" has exactly one recipe input (ore), so the edge landing on its Y-center is unique.
        var edge = layout.Edges.Single(e => e.X2 == sheetNode.X && e.Y2 == targetY);
        Assert.Equal(expectedLabel, edge.Label);
    }

    [Fact]
    public void AggregateRawMaterials_Sums_Quantities_Across_The_Whole_Pyramid()
    {
        var config = PilotConfig();
        var rebar = config.Materials["rebar"];

        var pyramid = CostCalculator.BuildInputPyramid(rebar, 1m, config.RecipeBook);
        var totals = MaterialChainDiagram.AggregateRawMaterials(pyramid);

        var ore = config.Materials["ore"];
        var oreTotal = totals.Single(entry => entry.Material == ore).Quantity;

        // rebar-from-sheet: 3 sheet -> 10 rebar; sheet-from-ore: 2 ore -> 1 sheet.
        // 1 rebar needs 0.3 sheet, 0.3 sheet needs 0.6 ore.
        Assert.Equal(0.6m, oreTotal);
    }
}
