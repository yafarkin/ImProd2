using Game.Config.Loading;
using Game.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>Раскладка цепочки материалов в SVG-координаты (запрос пользователя «отрисовка всей цепочки материалов») — над пилотным конфигом (Блок 9.3).</summary>
public class MaterialChainDiagramTests
{
    /// <summary>
    /// Маленький неизменный каталог (<c>tests/Fixtures/production-models/standard.json</c>): два
    /// сектора, пять материалов, руда → лист → арматура. Раскладку (узлы, столбцы, цвета, подписи
    /// рёбер) проверяем на нём, а не на боевой модели — правило геометрии не зависит от контента, а
    /// прибивать проверки вёрстки к живой цепочке значит ронять их при каждой правке баланса.
    /// </summary>
    private static ResolvedGameConfig LayoutFixture() => GameConfigLoader.LoadFromFiles(
        FixturePath("standard.json"), SessionPath);

    /// <summary>
    /// Боевая трёхсекторная модель — на ней проверяем ровно то, что без реального контента проверить
    /// нельзя: кросс-секторные рёбра, схождение веток и сквозные рёбра через границу сектора.
    /// </summary>
    private static ResolvedGameConfig GameConfig()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        return host.DefaultConfig;
    }

    /// <summary>
    /// <c>control-twin-metallurgy.json</c> — фикстура с сознательным сквозным ребром громадного
    /// пролёта (крепёж уровня 2 как прямой вход сборки коробки передач уровня 8). Живым моделям такой
    /// глубины больше нет (обе сокращены до 6-7 уровней 2026-09-07), а проверять верхнюю границу
    /// <see cref="MaterialChainDiagram.Edge.LevelSpan"/> на чём-то надо — поэтому файл и оставлен
    /// среди тестовых фикстур, хотя как играбельная цепочка он замещён.
    /// </summary>
    private static ResolvedGameConfig DeepChainFixture() => GameConfigLoader.LoadFromFiles(
        FixturePath("control-twin-metallurgy.json"), SessionPath);

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", fileName);

    private static string SessionPath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "pilot.json");

    [Fact]
    public void Build_Places_Every_Material_As_A_Node_And_Every_Recipe_Input_As_An_Edge()
    {
        var config = LayoutFixture();

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
        var config = LayoutFixture();
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
        var config = LayoutFixture();
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
        var config = LayoutFixture();
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
    /// Материал, у которого сходятся ветки из разных секторов, обязан рисоваться настоящим
    /// схождением: слагаемые лежат в РАЗНЫХ строках раскладки (у каждого материала внутри уровня
    /// своя строка, см. doc-comment <see cref="MaterialChainDiagram"/>), а цель — один узел с одной Y,
    /// значит хотя бы одно из входящих рёбер физически не может лечь горизонтально. Стальной каркас
    /// (А, уровень 5) собирается из своего листа (А), полимерного гранулята (Б) и фанеры (В) — три
    /// ветки, три сектора.
    /// </summary>
    [Fact]
    public void Build_Draws_A_Convergence_Point_As_Diagonals_From_Different_Rows()
    {
        var config = GameConfig();
        var layout = MaterialChainDiagram.Build(config);

        var sheet = layout.Nodes.Single(n => n.Material.Id == "steel-sheet");
        var granulate = layout.Nodes.Single(n => n.Material.Id == "polymer-granulate");
        var plywood = layout.Nodes.Single(n => n.Material.Id == "plywood");
        Assert.Equal(3, new[] { sheet.Y, granulate.Y, plywood.Y }.Distinct().Count());

        var incoming = layout.Edges.Where(e => e.TargetMaterialId == "steel-frame").ToList();
        Assert.Equal(3, incoming.Count);
        Assert.Contains(incoming, e => e.Y1 != e.Y2);
    }

    /// <summary>
    /// Связь между секторами — это ребро между узлами, которые в раскладке лежат в разных
    /// вертикальных блоках, поэтому оно всегда заметная длинная диагональ, а не короткая линия внутри
    /// одной ветки. Проверяем на трёх реальных импортных входах боевой модели, по одному на каждое
    /// принимающее направление, вместе с количеством из рецепта: количество кросс-входа ДЕЛИТСЯ со
    /// своим сырьём, а не добавляется сверху (правило
    /// <c>docs/production-chain-calibration-lessons.md</c>), поэтому оно дробное — круглая единица
    /// здесь была бы первым признаком вернувшегося аддитивного кросс-ребра.
    /// </summary>
    [Fact]
    public void Build_Draws_Cross_Sector_Links_As_Diagonals_With_A_Shared_Input_Quantity()
    {
        var config = GameConfig();
        var layout = MaterialChainDiagram.Build(config);

        foreach (var (sourceId, targetId, quantity) in new[]
                 {
                     ("industrial-gas", "crude-steel", 0.3m),   // Б -> А
                     ("pulp", "polymer-granulate", 0.3m),       // В -> Б
                     ("pig-iron", "laminated-beam", 0.15m),     // А -> В
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
            Assert.True(
                quantity < 1m,
                $"Кросс-вход {sourceId} -> {targetId} должен делить количество со своим сырьём, а не добавляться сверху.");
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
        var config = LayoutFixture();
        var sheet = config.Materials["sheet"];
        var recipe = config.RecipeBook.GetRecipe(sheet);
        var oreInput = recipe.Inputs.Single();

        var layout = MaterialChainDiagram.Build(config);

        var edge = layout.Edges.Single(e => e.TargetMaterialId == "sheet");
        Assert.Equal(oreInput.Material.Id, edge.SourceMaterialId);
        Assert.Equal("sheet", edge.TargetMaterialId);
        Assert.Equal(1, edge.LevelSpan); // ore (level 0) -> sheet (level 1), an ordinary adjacent-level step.
    }

    /// <summary>
    /// Тот же запрос пользователя, дальше: <see cref="MaterialChainDiagram.Edge.LevelSpan"/> должен
    /// отличать «сквозные» рёбра от обычных «соседних» — на нём страница решает, приглушать ли ребро
    /// по умолчанию. Проверяем на реальном сквозном ребре (крепёж уровня 2 — прямой вход сборки
    /// коробки передач уровня 8 в control-twin-metallurgy.json, минуя все промежуточные переделы).
    /// </summary>
    [Fact]
    public void Build_Marks_Skip_Level_Edges_With_A_LevelSpan_Greater_Than_One()
    {
        var config = DeepChainFixture();
        var layout = MaterialChainDiagram.Build(config);

        var fastenerSkipEdge = layout.Edges.Single(e => e.SourceMaterialId == "fasteners" && e.TargetMaterialId == "gearbox-assembly");
        Assert.Equal(6, fastenerSkipEdge.LevelSpan); // level 2 -> level 8.

        var adjacentEdge = layout.Edges.Single(e => e.SourceMaterialId == "forged-blanks" && e.TargetMaterialId == "machined-parts");
        Assert.Equal(1, adjacentEdge.LevelSpan); // level 6 -> level 7.
    }

    /// <summary>
    /// Взаимозависимость секторов должна идти во ВСЕ стороны, а не односторонне (обсуждение риска
    /// «сектор держит всех за яйца»): в боевой модели у каждого из шести направлений между тремя
    /// отраслями есть ровно по две связи, и у каждого сектора по четыре импортных входа и четыре
    /// экспортных. Раньше это проверялось поштучно на стадиях 2-4 плана раскрытия секторов; те файлы
    /// удалены 2026-09-07 вместе со стадиями, а само правило перенесено сюда и усилено — считаем не
    /// «есть хоть одна связь», а замкнутость треугольника целиком.
    /// </summary>
    [Fact]
    public void Build_Draws_All_Sector_Pairs_As_A_Closed_Ring_Of_Mutual_Dependency()
    {
        var config = GameConfig();

        var crossLinks = config.Materials.Values
            .Select(material => (Output: material, Recipe: config.RecipeBook.TryGetRecipe(material)))
            .Where(pair => pair.Recipe is not null)
            .SelectMany(pair => pair.Recipe!.Inputs.Select(input => (
                From: input.Material.Sector.Id,
                To: pair.Output.Sector.Id)))
            .Where(link => link.From != link.To)
            .ToList();

        var sectorIds = config.Sectors.Select(sector => sector.Id).ToList();
        Assert.Equal(3, sectorIds.Count);

        foreach (var from in sectorIds)
        {
            foreach (var to in sectorIds.Where(id => id != from))
            {
                Assert.True(
                    crossLinks.Count(link => link.From == from && link.To == to) >= 1,
                    $"Нет ни одной связи {from} -> {to}: треугольник разомкнут, сектор {to} ничего не покупает у {from}.");
            }
        }

        foreach (var sectorId in sectorIds)
        {
            Assert.True(crossLinks.Any(link => link.To == sectorId), $"Сектор {sectorId} ничего не импортирует.");
            Assert.True(crossLinks.Any(link => link.From == sectorId), $"Сектор {sectorId} ничего не экспортирует.");
        }
    }

    /// <summary>
    /// Сквозные рёбра пересекают не только уровни внутри отрасли, но и границу сектора: фанера
    /// (В, уровень 2) идёт напрямую и в стальной каркас (А, уровень 5), и в композитный корпус
    /// (Б, уровень 5), минуя все промежуточные переделы обеих отраслей. На таком ребре страница
    /// решает, приглушать ли его по умолчанию, поэтому <see cref="MaterialChainDiagram.Edge.LevelSpan"/>
    /// обязан считаться по разнице уровней, а не по факту «сосед/не сосед».
    /// </summary>
    [Fact]
    public void Build_Marks_Cross_Sector_Skip_Level_Edges_With_A_LevelSpan_Greater_Than_One()
    {
        var config = GameConfig();
        var layout = MaterialChainDiagram.Build(config);

        var intoFrame = layout.Edges.Single(e => e.SourceMaterialId == "plywood" && e.TargetMaterialId == "steel-frame");
        Assert.Equal(3, intoFrame.LevelSpan); // фанера (уровень 2) -> стальной каркас (уровень 5).

        var intoBody = layout.Edges.Single(e => e.SourceMaterialId == "plywood" && e.TargetMaterialId == "composite-body");
        Assert.Equal(3, intoBody.LevelSpan); // фанера (уровень 2) -> композитный корпус (уровень 5).

        var adjacent = layout.Edges.Single(e => e.SourceMaterialId == "steel-sheet" && e.TargetMaterialId == "steel-frame");
        Assert.Equal(1, adjacent.LevelSpan); // соседний уровень — не сквозное ребро.
    }

    [Fact]
    public void AggregateRawMaterials_Sums_Quantities_Across_The_Whole_Pyramid()
    {
        var config = LayoutFixture();
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
