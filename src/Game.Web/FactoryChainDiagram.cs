using Game.Domain;

namespace Game.Web;

/// <summary>
/// Раскладка производственной цепочки одного сектора в координаты для SVG-диаграммы — тот же
/// принцип раскладки по уровню материала, что и <see cref="MaterialChainDiagram"/>, но узлы — типы
/// фабрик сектора команды (<see cref="FactoryDefinition"/>), а не материалы каталога целиком: запрос
/// пользователя «не понятно с чего начинать» — карта того, что уже построено (сплошной узел) и что
/// можно построить дальше (пунктирный узел), а не абстрактный справочник материалов. Команда может
/// построить сколько угодно экземпляров одного типа — узел показывает их количество, а не подробности
/// конкретного экземпляра (те различаются: свой рецепт, свой уровень, свои рабочие — это в карточках
/// ниже, не на карте).
/// </summary>
public static class FactoryChainDiagram
{
    /// <summary>
    /// Один узел — тип фабрики сектора. <see cref="BuiltInstances"/> — экземпляры команды, если есть
    /// (может быть несколько); позиция и связи узла считаются по первому рецепту типа
    /// (<see cref="FactoryDefinition.Recipes"/>[0]), а не по фактически выбранным рецептам
    /// экземпляров — те у разных экземпляров могут отличаться, а узел на карте один на тип.
    /// </summary>
    public sealed record Node(
        FactoryDefinition Definition, IReadOnlyList<Factory> BuiltInstances,
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
    /// нет); <paramref name="builtFactories"/> — фабрики, реально построенные этой командой (может
    /// быть несколько экземпляров одного типа); <paramref name="sectorColor"/> — цвет сектора
    /// (<see cref="SectorColors"/>), тот же, что и на общей диаграмме материалов.
    /// </summary>
    public static Layout Build(
        IReadOnlyList<FactoryDefinition> sectorDefinitions, IReadOnlyList<Factory> builtFactories, string sectorColor)
    {
        ArgumentNullException.ThrowIfNull(sectorDefinitions);
        ArgumentNullException.ThrowIfNull(builtFactories);
        ArgumentNullException.ThrowIfNull(sectorColor);

        var builtByDefinitionId = builtFactories
            .GroupBy(factory => factory.Definition.Id)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Factory>)group.ToList());

        // Материал → фабрика, которая его производит (внутри этого сектора) — та же идея, что и
        // RecipeBook, но на уровень выше (по типу фабрики, а не по рецепту напрямую). Позиция и
        // связи узла всегда считаются по первому рецепту типа — построенные экземпляры могут выбрать
        // разные рецепты, но на карте у типа один узел, не по экземпляру.
        var recipeByDefinition = sectorDefinitions.ToDictionary(definition => definition, definition => definition.Recipes[0]);
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
                var builtInstances = builtByDefinitionId.GetValueOrDefault(definition.Id, Array.Empty<Factory>());
                var node = new Node(definition, builtInstances, x, y, NodeWidth, NodeHeight, sectorColor);
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
