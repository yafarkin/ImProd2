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

    private static Game.Config.Loading.ResolvedGameConfig DebugConfig()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        return host.DebugConfig;
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

    /// <summary>
    /// Регрессия на баг из ревью пользователя: на debug-конфиге узел наследовал строку от основного
    /// (наибольшего по количеству) входа своего рецепта, а не от алфавитного порядка названия — иначе
    /// «своя» линия ветки скачет по строкам и её не отличить на глаз от настоящей кросс-связи между
    /// ветками нефтехимии (Block 9.5). Проверяем, что обе ветки нефтехимии держат свою строку по всем
    /// 5 уровням (Y не меняется вдоль основной цепочки), а сами кросс-связи (второй, меньший вход
    /// рецепта из соседней ветки) остаются видимой диагональю.
    /// </summary>
    [Fact]
    public void Build_Keeps_Each_Petrochemical_Branch_On_Its_Own_Row_So_Cross_Branch_Inputs_Stand_Out_As_Diagonals()
    {
        var config = DebugConfig();
        var layout = MaterialChainDiagram.Build(config);

        double RowOf(string materialId) => layout.Nodes.Single(n => n.Material.Id == materialId).Y;

        var windowsBranch = new[] { "polyethylene", "pvc-film", "pvc-profile", "window-frame", "pvc-windows" };
        var tiresBranch = new[] { "synthetic-rubber", "rubber-compound", "tire-cord", "tire-carcass", "tires" };

        Assert.Single(windowsBranch.Select(RowOf).Distinct());
        Assert.Single(tiresBranch.Select(RowOf).Distinct());
        Assert.NotEqual(RowOf(windowsBranch[0]), RowOf(tiresBranch[0]));

        // Второй (кросс-ветковый) вход rubber-from-oil-ветки в pvc-профиле и наоборот — их источник
        // лежит в другой строке, значит ребро не горизонтальное.
        foreach (var (materialId, crossInputId) in new[]
                 {
                     ("pvc-film", "synthetic-rubber"),
                     ("rubber-compound", "polyethylene"),
                     ("window-frame", "tire-cord"),
                     ("tire-carcass", "pvc-profile"),
                 })
        {
            var target = layout.Nodes.Single(n => n.Material.Id == materialId);
            var source = layout.Nodes.Single(n => n.Material.Id == crossInputId);
            var edge = layout.Edges.Single(e =>
                e.X1 == source.X + source.Width && e.Y1 == source.Y + source.Height / 2 &&
                e.X2 == target.X && e.Y2 == target.Y + target.Height / 2);

            Assert.NotEqual(edge.Y1, edge.Y2);
        }
    }

    /// <summary>
    /// Связь между секторами «Металлургия» и «Нефтегазохимия» (запрос пользователя «где у нас идёт
    /// связь металлургов и нефтехимией», Block 9.5): добыча железа/меди берёт нефть как топливо, а
    /// заготовки шин — медную проволоку как металлокорд. Обе стороны — рёбра между узлами разных
    /// секторов, которые в раскладке лежат в разных вертикальных блоках, поэтому такое ребро всегда
    /// заметная длинная диагональ, а не короткая линия внутри одной ветки.
    /// </summary>
    [Fact]
    public void Build_Draws_Cross_Sector_Links_Between_Metallurgy_And_Petrochemistry()
    {
        var config = DebugConfig();
        var layout = MaterialChainDiagram.Build(config);

        foreach (var (sourceId, targetId, quantity) in new[]
                 {
                     ("oil", "iron", 5m),
                     ("oil", "copper", 10m),
                     ("copper-wire", "tire-carcass", 5m),
                 })
        {
            var source = layout.Nodes.Single(n => n.Material.Id == sourceId);
            var target = layout.Nodes.Single(n => n.Material.Id == targetId);
            Assert.NotEqual(source.Material.Sector.Id, target.Material.Sector.Id);

            var edge = layout.Edges.Single(e =>
                e.X1 == source.X + source.Width && e.Y1 == source.Y + source.Height / 2 &&
                e.X2 == target.X && e.Y2 == target.Y + target.Height / 2);

            Assert.NotEqual(edge.Y1, edge.Y2);
            var recipe = config.RecipeBook.GetRecipe(target.Material);
            Assert.Equal(quantity, recipe.Inputs.Single(input => input.Material.Id == sourceId).Quantity);
        }
    }

    /// <summary>
    /// Запрос пользователя: на глубоких цепочках со сквозными рёбрами (материал N-го уровня как вход
    /// рецепта на уровне N+7 и глубже) полный граф превращается в паутину — странице нужно уметь
    /// показать только рёбра выбранного материала. Для этого <see cref="MaterialChainDiagram.Edge"/>
    /// обязан нести коды обоих концов ребра, а не только геометрию.
    /// </summary>
    [Fact]
    public void Build_Labels_Each_Edge_With_The_Material_Ids_Of_Both_Ends()
    {
        var config = PilotConfig();
        var sheet = config.Materials["sheet"];
        var recipe = config.RecipeBook.GetRecipe(sheet);
        var oreInput = recipe.Inputs.Single();

        var layout = MaterialChainDiagram.Build(config);

        var edge = layout.Edges.Single(e => e.TargetMaterialId == "sheet");
        Assert.Equal(oreInput.Material.Id, edge.SourceMaterialId);
        Assert.Equal("sheet", edge.TargetMaterialId);
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
