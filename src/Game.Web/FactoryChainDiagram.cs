using Game.Domain;

namespace Game.Web;

/// <summary>
/// Раскладка производственной цепочки одного сектора в координаты для SVG-диаграммы — тот же
/// принцип раскладки по уровню материала, что и <see cref="MaterialChainDiagram"/>, но узлы — типы
/// фабрик сектора команды (<see cref="FactoryDefinition"/>), а не материалы каталога целиком: запрос
/// пользователя «не понятно с чего начинать» — карта того, что уже построено (сплошной узел) и что
/// можно построить дальше (пунктирный узел), а не абстрактный справочник материалов.
/// </summary>
public static class FactoryChainDiagram
{
    /// <summary>
    /// Один узел — тип фабрики сектора. <see cref="Built"/> — построенный экземпляр команды, если
    /// есть; <see cref="Recipe"/> — его фактический выбранный рецепт (построена) или рецепт по
    /// умолчанию, первый в списке (не построена, тем же правилом, что и конструктор <see cref="Factory"/>).
    /// </summary>
    public sealed record Node(
        FactoryDefinition Definition, Factory? Built, Recipe Recipe,
        double X, double Y, double Width, double Height, string Color);

    /// <summary>Одна связь «вход рецепта» — координаты для кривой и подпись с нормированным количеством (на 1 единицу выхода).</summary>
    public sealed record Edge(double X1, double Y1, double X2, double Y2, string Label);

    /// <summary>Итоговая раскладка целиком — узлы, связи и размер холста.</summary>
    public sealed record Layout(IReadOnlyList<Node> Nodes, IReadOnlyList<Edge> Edges, double Width, double Height);

    private const double ColumnWidth = 220;
    private const double NodeWidth = 170;
    private const double NodeHeight = 50;
    private const double RowHeight = 70;
    private const double Margin = 40;

    /// <summary>
    /// <paramref name="sectorDefinitions"/> — все типы фабрик сектора команды (построенные и ещё
    /// нет); <paramref name="builtFactories"/> — фабрики, реально построенные этой командой;
    /// <paramref name="sectorColor"/> — цвет сектора (<see cref="SectorColors"/>), тот же, что и на
    /// общей диаграмме материалов.
    /// </summary>
    public static Layout Build(
        IReadOnlyList<FactoryDefinition> sectorDefinitions, IReadOnlyList<Factory> builtFactories, string sectorColor)
    {
        ArgumentNullException.ThrowIfNull(sectorDefinitions);
        ArgumentNullException.ThrowIfNull(builtFactories);
        ArgumentNullException.ThrowIfNull(sectorColor);

        var builtByDefinitionId = builtFactories.ToDictionary(factory => factory.Definition.Id);
        var recipeByDefinition = sectorDefinitions.ToDictionary(
            definition => definition,
            definition => builtByDefinitionId.TryGetValue(definition.Id, out var factory) ? factory.SelectedRecipe : definition.Recipes[0]);

        // Материал → фабрика, которая его производит (внутри этого сектора) — та же идея, что и
        // RecipeBook, но на уровень выше (по типу фабрики, а не по рецепту напрямую).
        var producerByMaterial = sectorDefinitions
            .SelectMany(definition => definition.Recipes.Select(recipe => (recipe.Output, Definition: definition)))
            .ToDictionary(entry => entry.Output, entry => entry.Definition);

        var byLevel = sectorDefinitions
            .GroupBy(definition => recipeByDefinition[definition].Output.Level)
            .OrderBy(group => group.Key)
            .ToList();

        var nodes = new List<Node>();
        var nodeByDefinitionId = new Dictionary<string, Node>();
        var maxY = Margin;

        foreach (var levelGroup in byLevel)
        {
            var x = Margin + levelGroup.Key * ColumnWidth;
            var ordered = levelGroup.OrderBy(definition => definition.Name, StringComparer.Ordinal).ToList();

            var y = Margin;
            foreach (var definition in ordered)
            {
                builtByDefinitionId.TryGetValue(definition.Id, out var built);
                var node = new Node(definition, built, recipeByDefinition[definition], x, y, NodeWidth, NodeHeight, sectorColor);
                nodes.Add(node);
                nodeByDefinitionId[definition.Id] = node;

                y += RowHeight;
            }

            maxY = Math.Max(maxY, y);
        }

        var edges = new List<Edge>();
        foreach (var definition in sectorDefinitions)
        {
            var recipe = recipeByDefinition[definition];
            var target = nodeByDefinitionId[definition.Id];
            foreach (var input in recipe.Inputs)
            {
                // Вход мог быть из другого сектора (кросс-секторная зависимость, см. doc-comment
                // FactoryDefinition) — эта диаграмма ограничена одним сектором команды, такую связь
                // просто не рисуем, а не пытаемся притянуть чужой сектор.
                if (!producerByMaterial.TryGetValue(input.Material, out var producerDefinition))
                {
                    continue;
                }

                var source = nodeByDefinitionId[producerDefinition.Id];
                var ratioPerUnit = input.Quantity / recipe.OutputQuantity;
                edges.Add(new Edge(
                    source.X + source.Width, source.Y + source.Height / 2,
                    target.X, target.Y + target.Height / 2,
                    FormatRatio(ratioPerUnit)));
            }
        }

        var maxLevel = byLevel.Count == 0 ? 0 : byLevel.Max(group => group.Key);
        var width = Margin + maxLevel * ColumnWidth + NodeWidth + Margin;
        var height = maxY + Margin;

        return new Layout(nodes, edges, width, height);
    }

    private static string FormatRatio(decimal ratioPerUnit) => "×" + ratioPerUnit.ToString("0.##");
}
