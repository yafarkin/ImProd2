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

    /// <summary>
    /// metallurgy.json — production-модель с сознательными сквозными рёбрами (крепёж уровня 2 как
    /// прямой вход сборок уровня 8-9), нужна отдельно от <see cref="DefaultConfig"/>/<see
    /// cref="DebugConfig"/> для проверки <see cref="MaterialChainDiagram.Edge.LevelSpan"/>.
    /// </summary>
    private static Game.Config.Loading.ResolvedGameConfig MetallurgyConfig()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        return Game.Config.Loading.GameConfigLoader.Load(host.ProductionModels["metallurgy"], host.SessionConfigs["pilot"]);
    }

    /// <summary>
    /// metallurgy-petrochemistry.json — стадия 2 плана раскрытия секторов (`docs/production-staging.md`):
    /// у каждой отрасли своя заготовка большого продукта (автомобиль/катер), обе тянут материалы у
    /// соседней отрасли — взаимно, а не односторонне.
    /// </summary>
    private static Game.Config.Loading.ResolvedGameConfig MetallurgyPetrochemistryConfig()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        return Game.Config.Loading.GameConfigLoader.Load(
            host.ProductionModels["metallurgy-petrochemistry"], host.SessionConfigs["pilot"]);
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
    /// Ревизия нефтехимии (запрос пользователя: ПВХ реально не делают из полиэтилена, шинный корд не
    /// имеет отношения к оконным рамам — обе связи в старой версии debug.json существовали только
    /// ради диагонали на диаграмме, не по химии). Настоящая конвергенция двух веток нефтехимии
    /// (пластиковой и резиновой) теперь — «Композитные материалы» (SPEC production-chains.md: «из
    /// Пластика + Резины»). ПВХ-профиль и шинный корд — разные материалы одного уровня, у каждого
    /// материала внутри уровня своя строка (см. doc-comment <see cref="MaterialChainDiagram"/>), а
    /// «Композитные материалы» — один узел с одной Y — значит хотя бы один из двух входов физически не
    /// может лечь горизонтально и обязан остаться видимой диагональю, какой бы из двух рецепт ни
    /// выбрал «своим» при сортировке строк.
    /// </summary>
    [Fact]
    public void Build_Draws_Composite_Material_As_A_Convergence_Of_Both_Petrochemical_Branches()
    {
        var config = DebugConfig();
        var layout = MaterialChainDiagram.Build(config);

        var pvcProfile = layout.Nodes.Single(n => n.Material.Id == "pvc-profile");
        var tireCord = layout.Nodes.Single(n => n.Material.Id == "tire-cord");
        Assert.NotEqual(pvcProfile.Y, tireCord.Y); // разные ветки — разные строки.

        var fromPvcProfile = layout.Edges.Single(e => e.SourceMaterialId == "pvc-profile" && e.TargetMaterialId == "composite-material");
        var fromTireCord = layout.Edges.Single(e => e.SourceMaterialId == "tire-cord" && e.TargetMaterialId == "composite-material");
        Assert.True(
            fromPvcProfile.Y1 != fromPvcProfile.Y2 || fromTireCord.Y1 != fromTireCord.Y2,
            "At least one of the two inputs into composite-material must render as a diagonal cross-branch edge.");
    }

    /// <summary>
    /// Связь между секторами «Металлургия» и «Нефтегазохимия» (запрос пользователя «где у нас идёт
    /// связь металлургов и нефтехимией», Block 9.5, ревизия — запрос пользователя: у шин настоящий
    /// корд стальной, не медный): добыча железа/меди берёт нефть как топливо, заготовки шин — стальную
    /// проволоку как металлокорд, а корпус судна — металлоконструкции как балласт/такелажную оснастку.
    /// Все три — рёбра между узлами разных секторов, которые в раскладке лежат в разных вертикальных
    /// блоках, поэтому такое ребро всегда заметная длинная диагональ, а не короткая линия внутри одной
    /// ветки.
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
                     ("steel-wire", "tire-carcass", 2m),
                     ("steel-structures", "hull", 2m),
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
    /// Запрос пользователя: сделать в самой нефтехимии связь «фабрика N берёт материал не с
    /// предыдущего уровня, а издалека», по образцу крепежа в metallurgy.json. Технический углерод
    /// (сажа, уровень 1) и клеевой состав из метанола (уровень 2) — оба реалистичные шинные
    /// добавки — идут напрямую в заготовки шин (уровень 4), минуя уровни между ними.
    /// </summary>
    [Fact]
    public void Build_Marks_Petrochemical_Skip_Level_Edges_With_LevelSpan_Greater_Than_One()
    {
        var config = DebugConfig();
        var layout = MaterialChainDiagram.Build(config);

        var carbonBlackSkip = layout.Edges.Single(e => e.SourceMaterialId == "carbon-black" && e.TargetMaterialId == "tire-carcass");
        Assert.Equal(3, carbonBlackSkip.LevelSpan); // carbon-black (level 1) -> tire-carcass (level 4).

        var adhesiveSkip = layout.Edges.Single(e => e.SourceMaterialId == "cord-adhesive" && e.TargetMaterialId == "tire-carcass");
        Assert.Equal(2, adhesiveSkip.LevelSpan); // cord-adhesive (level 2) -> tire-carcass (level 4).
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
        Assert.Equal(1, edge.LevelSpan); // ore (level 0) -> sheet (level 1), an ordinary adjacent-level step.
    }

    /// <summary>
    /// Тот же запрос пользователя, дальше: <see cref="MaterialChainDiagram.Edge.LevelSpan"/> должен
    /// отличать «сквозные» рёбра от обычных «соседних» — на нём страница решает, приглушать ли ребро
    /// по умолчанию. Проверяем на реальном сквозном ребре (крепёж уровня 2 — прямой вход сборки
    /// коробки передач уровня 8 в metallurgy.json, минуя все промежуточные переделы).
    /// </summary>
    [Fact]
    public void Build_Marks_Skip_Level_Edges_With_A_LevelSpan_Greater_Than_One()
    {
        var config = MetallurgyConfig();
        var layout = MaterialChainDiagram.Build(config);

        var fastenerSkipEdge = layout.Edges.Single(e => e.SourceMaterialId == "fasteners" && e.TargetMaterialId == "gearbox-assembly");
        Assert.Equal(6, fastenerSkipEdge.LevelSpan); // level 2 -> level 8.

        var adjacentEdge = layout.Edges.Single(e => e.SourceMaterialId == "forged-blanks" && e.TargetMaterialId == "machined-parts");
        Assert.Equal(1, adjacentEdge.LevelSpan); // level 6 -> level 7.
    }

    /// <summary>
    /// Стадия 2 плана (`docs/production-staging.md`): рычаг должен идти в обе стороны, не
    /// односторонне (запрос пользователя — обсуждение риска «сектор держит всех за яйца»). Автомобиль
    /// (сектор А) тянет шины/эмаль/бензин у Б, катер (сектор Б) тянет двигатель/крепёж у А — оба
    /// флагмана зависят от чужого сектора, ни один не самодостаточен.
    /// </summary>
    [Fact]
    public void Build_Draws_Metallurgy_Petrochemistry_Flagships_As_Mutually_Dependent_On_Both_Sectors()
    {
        var config = MetallurgyPetrochemistryConfig();
        var layout = MaterialChainDiagram.Build(config);

        var automobile = config.Materials["automobile"];
        var automobileRecipe = config.RecipeBook.GetRecipe(automobile);
        Assert.Equal("A", automobile.Sector.Id);
        foreach (var petrochemicalInput in new[] { "tires", "paint", "gasoline" })
        {
            Assert.Contains(automobileRecipe.Inputs, input => input.Material.Id == petrochemicalInput);
        }

        var boat = config.Materials["boat"];
        var boatRecipe = config.RecipeBook.GetRecipe(boat);
        Assert.Equal("B", boat.Sector.Id);
        foreach (var metallurgyInput in new[] { "engine", "fasteners" })
        {
            Assert.Contains(boatRecipe.Inputs, input => input.Material.Id == metallurgyInput);
        }

        // Оба перекрёстных ребра — между разными столбцами секторов, значит заметная диагональ, не короткая линия.
        foreach (var (sourceId, targetId) in new[] { ("tires", "automobile"), ("engine", "boat") })
        {
            var source = layout.Nodes.Single(n => n.Material.Id == sourceId);
            var target = layout.Nodes.Single(n => n.Material.Id == targetId);
            Assert.NotEqual(source.Material.Sector.Id, target.Material.Sector.Id);
        }
    }

    /// <summary>
    /// Ещё один вариант той же идеи: сквозные рёбра теперь идут и МЕЖДУ секторами, не только внутри
    /// одного — бензин (Б, уровень 1) и эмаль (Б, уровень 2) идут напрямую в автомобиль (А, уровень 7),
    /// минуя все промежуточные переделы обеих отраслей.
    /// </summary>
    [Fact]
    public void Build_Marks_Cross_Sector_Skip_Level_Edges_Into_The_Automobile()
    {
        var config = MetallurgyPetrochemistryConfig();
        var layout = MaterialChainDiagram.Build(config);

        var gasolineSkip = layout.Edges.Single(e => e.SourceMaterialId == "gasoline" && e.TargetMaterialId == "automobile");
        Assert.Equal(6, gasolineSkip.LevelSpan); // level 1 -> level 7.

        var paintSkip = layout.Edges.Single(e => e.SourceMaterialId == "paint" && e.TargetMaterialId == "automobile");
        Assert.Equal(5, paintSkip.LevelSpan); // level 2 -> level 7.
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
