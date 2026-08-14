using Game.Config.Loading;
using Game.Domain;

namespace Game.Web;

/// <summary>
/// Раскладка полной цепочки материалов конфига в координаты для SVG-диаграммы (запрос пользователя
/// «отрисовка всей цепочки материалов, от руды до конца») — чистая функция над
/// <see cref="ResolvedGameConfig"/>, без собственного состояния, по тому же принципу, что и
/// <see cref="DashboardDisplay"/>. Материалы раскладываются по столбцам согласно
/// <see cref="Material.Level"/> (сырьё слева, готовая продукция справа — граф гарантированно без
/// циклов, см. doc-comment <see cref="RecipeBook"/> и валидатор конфига), внутри столбца
/// группируются по сектору. Внутри сектора строка материала наследуется от строки его основного
/// (по наибольшему количеству во входах рецепта) предшественника — так каждая производственная линия
/// идёт по столбцам горизонтально, а не «прыгает» по алфавиту названия. Это принципиально там, где у
/// рецепта два входа из разных линий (запрос пользователя «пересечения между ветками» — Block 9.5):
/// побочный вход с меньшим количеством должен остаться заметной диагональю, а не быть перепутан с
/// основной линией из-за случайного алфавитного порядка — иначе на картинке пересечения не видно.
/// </summary>
public static class MaterialChainDiagram
{
    /// <summary>Один узел диаграммы — материал и его геометрия/цвет.</summary>
    public sealed record Node(Material Material, double X, double Y, double Width, double Height, string Color);

    /// <summary>
    /// Одна связь «вход рецепта» — координаты для кривой, подпись с нормированным количеством (на 1
    /// единицу выхода) и коды обоих концов (<see cref="SourceMaterialId"/>/<see
    /// cref="TargetMaterialId"/>) — нужны странице, чтобы при клике на материал скрыть все рёбра,
    /// кроме связанных с ним (запрос пользователя: на глубоких цепочках со сквозными рёбрами через
    /// много уровней полный граф превращается в паутину из пересекающихся линий и подписей).
    /// </summary>
    public sealed record Edge(double X1, double Y1, double X2, double Y2, string Label, string SourceMaterialId, string TargetMaterialId);

    /// <summary>Итоговая раскладка целиком — узлы, связи и размер холста.</summary>
    public sealed record Layout(IReadOnlyList<Node> Nodes, IReadOnlyList<Edge> Edges, double Width, double Height);

    private const double ColumnWidth = 220;
    private const double NodeWidth = 160;
    private const double NodeHeight = 44;
    private const double RowHeight = 64;
    private const double SectorGap = 24;
    private const double Margin = 40;

    public static Layout Build(ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var sectorColor = SectorColors.ForSectors(config.Sectors);
        var sectorOrder = config.Sectors
            .Select((sector, index) => (sector.Id, Order: index))
            .ToDictionary(entry => entry.Id, entry => entry.Order);

        var materialsByLevel = config.Materials.Values
            .GroupBy(material => material.Level)
            .OrderBy(group => group.Key)
            .ToList();

        var nodes = new List<Node>();
        var nodeByMaterialId = new Dictionary<string, Node>();
        var maxY = Margin;

        foreach (var levelGroup in materialsByLevel)
        {
            var x = Margin + levelGroup.Key * ColumnWidth;
            var ordered = levelGroup
                .OrderBy(material => sectorOrder.GetValueOrDefault(material.Sector.Id))
                .ThenBy(material => PrimaryPredecessorY(material, config, nodeByMaterialId) ?? double.MaxValue)
                .ThenBy(material => material.Name, StringComparer.Ordinal)
                .ToList();

            var y = Margin;
            string? previousSectorId = null;
            foreach (var material in ordered)
            {
                if (previousSectorId is not null && previousSectorId != material.Sector.Id)
                {
                    y += SectorGap;
                }
                previousSectorId = material.Sector.Id;

                var node = new Node(material, x, y, NodeWidth, NodeHeight, sectorColor[material.Sector.Id]);
                nodes.Add(node);
                nodeByMaterialId[material.Id] = node;

                y += RowHeight;
            }

            maxY = Math.Max(maxY, y);
        }

        var edges = new List<Edge>();
        foreach (var material in config.Materials.Values)
        {
            var recipe = config.RecipeBook.TryGetRecipe(material);
            if (recipe is null)
            {
                continue;
            }

            var target = nodeByMaterialId[material.Id];
            foreach (var input in recipe.Inputs)
            {
                var source = nodeByMaterialId[input.Material.Id];
                var ratioPerUnit = input.Quantity / recipe.OutputQuantity;
                edges.Add(new Edge(
                    source.X + source.Width, source.Y + source.Height / 2,
                    target.X, target.Y + target.Height / 2,
                    FormatRatio(ratioPerUnit),
                    input.Material.Id, material.Id));
            }
        }

        var maxLevel = materialsByLevel.Count == 0 ? 0 : materialsByLevel.Max(group => group.Key);
        var width = Margin + maxLevel * ColumnWidth + NodeWidth + Margin;
        var height = maxY + Margin;

        return new Layout(nodes, edges, width, height);
    }

    /// <summary>
    /// Суммирует потребность в сырье по всей пирамиде входов (<see cref="CostCalculator.BuildInputPyramid"/>)
    /// в один список «материал → итоговое количество» — общий итог по каждому виду сырья, а не
    /// повторяющиеся ветки дерева, для ответа на вопрос «сколько в целом надо руды».
    /// </summary>
    public static IReadOnlyList<(Material Material, decimal Quantity)> AggregateRawMaterials(InputPyramidNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var totals = new Dictionary<Material, decimal>();
        Collect(root, totals);

        return totals
            .Select(entry => (entry.Key, entry.Value))
            .OrderBy(entry => entry.Key.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static void Collect(InputPyramidNode node, Dictionary<Material, decimal> totals)
    {
        if (node.Material.IsRawMaterial)
        {
            totals[node.Material] = totals.GetValueOrDefault(node.Material) + node.Quantity;
        }

        foreach (var input in node.Inputs)
        {
            Collect(input, totals);
        }
    }

    private static string FormatRatio(decimal ratioPerUnit) => "×" + ratioPerUnit.ToString("0.##");

    /// <summary>
    /// Y уже размещённого узла материала, который вносит наибольший вклад (по количеству во входе)
    /// в рецепт данного материала — «своя» производственная линия, а не побочный/кросс-вход. Null для
    /// сырья (нет рецепта) и для случаев, когда предшественник почему-то ещё не размещён (в норме не
    /// бывает — уровни обрабатываются по возрастанию, а вход рецепта всегда более низкого уровня).
    /// </summary>
    private static double? PrimaryPredecessorY(
        Material material, ResolvedGameConfig config, IReadOnlyDictionary<string, Node> nodeByMaterialId)
    {
        var recipe = config.RecipeBook.TryGetRecipe(material);
        if (recipe is null || recipe.Inputs.Count == 0)
        {
            return null;
        }

        // OrderByDescending стабилен: при равном количестве побеждает вход, объявленный в рецепте
        // первым (по конвенции конфига — это и есть «свой» ингредиент, см. Samples/production-models/debug.json).
        var primaryInput = recipe.Inputs.OrderByDescending(input => input.Quantity).First();

        return nodeByMaterialId.TryGetValue(primaryInput.Material.Id, out var predecessorNode)
            ? predecessorNode.Y
            : null;
    }
}
