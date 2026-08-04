using Game.Domain;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// Единая раскладка фабрик сектора команды в координаты для SVG-диаграммы — объединяет то, что раньше
/// было двумя разными диаграммами (<c>FactoryChainDiagram</c> — типы сектора и что ещё можно
/// построить, <c>FactoryInstanceDiagram</c> — только построенные экземпляры) в один граф (запрос
/// пользователя: «слева направо по уровню, построенное и то, что можно построить — вместе»). Склад
/// команды общий на все фабрики (см. <see cref="Warehouse"/>), поэтому стрелок «кто что потребляет»
/// здесь больше нет — вместо них у построенного узла есть <see cref="Node.Status"/>: хватает ли ему
/// сырья, чтобы выйти на полную мощность прямо сейчас.
/// </summary>
public static class FactoryOverviewDiagram
{
    /// <summary>
    /// Состояние узла: <see cref="NotBuilt"/> — пунктирный узел «построить» (есть всегда, для
    /// каждого типа сектора, независимо от того, сколько экземпляров уже построено);
    /// <see cref="Adequate"/> — построенному экземпляру хватает сырья на полную мощность (или ещё
    /// нет оценки — тогда тревогу не поднимаем); <see cref="ShortOfInput"/> — фактический выпуск
    /// меньше того, что позволили бы рабочие и уровень, из-за нехватки сырья на общем складе.
    /// </summary>
    public enum LoadStatus { NotBuilt, Adequate, ShortOfInput }

    /// <summary>
    /// Один узел диаграммы. <see cref="Instance"/> равен <c>null</c> для узла-плейсхолдера
    /// «построить ещё» — тогда <see cref="IndexWithinType"/> и <see cref="Profit"/> тоже
    /// не заданы. Для построенного экземпляра <see cref="IndexWithinType"/> — порядковый номер среди
    /// экземпляров того же типа (1, 2, ...), <see cref="Profit"/> — оценка прибыли за тик
    /// (<see cref="FactoryProfitabilityCalculator"/>), либо <c>null</c>, если рыночной котировки для
    /// оценки ещё нет.
    /// </summary>
    public sealed record Node(
        FactoryDefinition Definition, Factory? Instance, int? IndexWithinType,
        LoadStatus Status, decimal? Profit,
        double X, double Y, double Width, double Height);

    /// <summary>Итоговая раскладка целиком — узлы и размер холста (без связей — см. doc-comment класса).</summary>
    public sealed record Layout(IReadOnlyList<Node> Nodes, double Width, double Height);

    private const double ColumnWidth = 220;
    private const double NodeWidth = 170;
    private const double NodeHeight = 62;
    private const double RowHeight = 86;
    private const double Margin = 40;

    /// <summary>
    /// <paramref name="sectorDefinitions"/> — все типы фабрик сектора команды (построенные и ещё
    /// нет); <paramref name="builtFactories"/> — фактически построенные фабрики этой команды (может
    /// быть несколько экземпляров одного типа); <paramref name="profitabilityByFactoryId"/> —
    /// заранее посчитанная оценка прибыльности каждого построенного экземпляра
    /// (<see cref="FactoryProfitabilityCalculator.TryCalculate"/>) — отсутствие ключа даёт
    /// <see cref="LoadStatus.Adequate"/> без вывода прибыли (ещё не считали, не значит «плохо»).
    /// </summary>
    public static Layout Build(
        IReadOnlyList<FactoryDefinition> sectorDefinitions,
        IReadOnlyList<Factory> builtFactories,
        IReadOnlyDictionary<Ulid, FactoryProfitabilityCalculator.FactoryProfitabilityEstimate> profitabilityByFactoryId)
    {
        ArgumentNullException.ThrowIfNull(sectorDefinitions);
        ArgumentNullException.ThrowIfNull(builtFactories);
        ArgumentNullException.ThrowIfNull(profitabilityByFactoryId);

        var builtByDefinitionId = builtFactories
            .GroupBy(factory => factory.Definition.Id)
            .ToDictionary(group => group.Key, group => group.OrderBy(factory => factory.Id).ToList());

        // Позиция типа по уровню считается по первому рецепту типа (как раньше в FactoryChainDiagram)
        // — построенные экземпляры этого типа могут выбрать другой рецепт, но и они, и плейсхолдер
        // «построить ещё» обязаны оказаться в одной колонке, иначе плейсхолдер «убегает» от своих же
        // построенных экземпляров при смене ими рецепта.
        var byLevel = sectorDefinitions
            .GroupBy(definition => definition.Recipes[0].Output.Level)
            .OrderBy(group => group.Key)
            .ToList();

        var nodes = new List<Node>();
        var maxY = Margin;

        foreach (var levelGroup in byLevel)
        {
            var x = Margin + levelGroup.Key * ColumnWidth;
            var y = Margin;

            foreach (var definition in levelGroup.OrderBy(definition => definition.Name, StringComparer.Ordinal))
            {
                var built = builtByDefinitionId.GetValueOrDefault(definition.Id, []);
                var index = 1;
                foreach (var instance in built)
                {
                    var (status, profit) = ResolveStatus(instance, profitabilityByFactoryId);
                    nodes.Add(new Node(definition, instance, index, status, profit, x, y, NodeWidth, NodeHeight));
                    y += RowHeight;
                    index++;
                }

                // Плейсхолдер «построить ещё» — ровно один на тип, даже если экземпляры уже есть
                // (запрос пользователя: «он должен быть, даже если фабрики уже построены»).
                nodes.Add(new Node(definition, null, null, LoadStatus.NotBuilt, null, x, y, NodeWidth, NodeHeight));
                y += RowHeight;
            }

            maxY = Math.Max(maxY, y);
        }

        var maxLevel = byLevel.Count == 0 ? 0 : byLevel.Max(group => group.Key);
        var width = Margin + maxLevel * ColumnWidth + NodeWidth + Margin;
        var height = maxY + Margin;

        return new Layout(nodes, width, height);
    }

    private static (LoadStatus Status, decimal? Profit) ResolveStatus(
        Factory factory,
        IReadOnlyDictionary<Ulid, FactoryProfitabilityCalculator.FactoryProfitabilityEstimate> profitabilityByFactoryId)
    {
        if (!profitabilityByFactoryId.TryGetValue(factory.Id, out var estimate))
        {
            return (LoadStatus.Adequate, null);
        }

        var status = estimate.ProjectedOutputQuantity < estimate.CapacityLimitedOutputQuantity
            ? LoadStatus.ShortOfInput
            : LoadStatus.Adequate;
        var profit = estimate.HasPriceSignal ? estimate.Profit : (decimal?)null;

        return (status, profit);
    }
}
