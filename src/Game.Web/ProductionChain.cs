using Game.Domain;

namespace Game.Web;

/// <summary>
/// Раздел «Производство» как цепочка (docs/manager-ui/README.md §4, блок 3): переделы сверху вниз,
/// внутри передела — типы фабрик, внутри типа — экземпляры, а после каждого передела — полоса склада
/// с тем, что он производит. Раньше фабрики шли одним плоским списком, а склад был отдельной вкладкой
/// без связи «кто это производит и кто потребляет» (живой обход 2026-10-04, §2 п.8): железная руда
/// копилась тысячами, и со склада не было видно, что её не берёт ни одна фабрика.
/// <para>
/// Только подача: всё, что здесь собрано, уже посчитано движком или моделью экрана — список узлов
/// (<see cref="FactoryOverviewList"/>), поводы «Требует внимания», выпуск и потребление прошлого хода
/// из журнала.
/// </para>
/// </summary>
public static class ProductionChain
{
    /// <summary>
    /// При стольких построенных фабриках и меньше вся цепочка помещается на экран телефона, и
    /// сворачивать переделы без проблем незачем — свёрнутая по умолчанию цепочка из двух фабрик
    /// только прячет от новичка кнопки постройки. Порог подачи, не игровая величина.
    /// </summary>
    public const int SmallChainFactoryCount = 5;

    /// <summary>Передел цепочки. <see cref="StockLines"/> — материалы, которые производят фабрики этого передела.</summary>
    public sealed record Level(
        int Number,
        IReadOnlyList<TypeGroup> Types,
        IReadOnlyList<StockLine> StockLines,
        int FactoryCount,
        decimal LastTurnOutput,
        int ProblemCount,
        bool OpenByDefault);

    /// <summary>
    /// Однотипные фабрики передела — «Рудник ×3» одной строкой с суммарным выпуском и худшим
    /// состоянием; у непостроенного типа <see cref="Instances"/> пуст, он нужен ради кнопки постройки.
    /// </summary>
    public sealed record TypeGroup(
        FactoryDefinition Definition,
        IReadOnlyList<Instance> Instances,
        int Workers,
        decimal LastTurnOutput,
        decimal MaxOutput,
        decimal WorstCondition,
        int ProblemCount);

    /// <summary>
    /// Один построенный экземпляр. <see cref="LastTurnOutput"/> — <c>null</c>, пока не было ни одного
    /// расчёта; <see cref="Problems"/> — короткие причины из «Требует внимания», без имени фабрики.
    /// </summary>
    public sealed record Instance(
        Ulid FactoryId,
        string Title,
        int Workers,
        decimal Condition,
        bool IsUnderRepair,
        decimal? LastTurnOutput,
        decimal MaxOutput,
        IReadOnlyList<string> Problems);

    /// <summary>
    /// Материал на складе и его движение за прошлый ход: сколько произвели свои фабрики и кто сколько
    /// забрал. <see cref="ProducerNames"/>/<see cref="ConsumerNames"/> — типы своих построенных фабрик,
    /// без повторов; пустой список — «ни одна ваша фабрика».
    /// </summary>
    public sealed record StockLine(
        Material Material,
        decimal Quantity,
        decimal ProducedLastTurn,
        IReadOnlyList<(string Name, decimal Quantity)> ConsumedLastTurn,
        IReadOnlyList<string> ProducerNames,
        IReadOnlyList<string> ConsumerNames);

    /// <summary>Закрытые исследованием поколений переделы — одной строкой «замка», а не пустыми блоками.</summary>
    public sealed record LockedLevels(int FirstLevel, int LastLevel, IReadOnlyList<string> TypeNames);

    /// <summary>
    /// Цепочка целиком. <see cref="OtherStock"/> — материалы, которые не производит ни один тип фабрик
    /// сектора (куплены по контракту или аварийно): им нет места между переделами.
    /// <see cref="HasLastTurn"/> — был ли уже расчёт с производством: до него движение склада «+0 · −0»
    /// было бы неправдой, и подписи говорят только, кто производит и кто потребляет.
    /// </summary>
    public sealed record Chain(
        IReadOnlyList<Level> Levels,
        LockedLevels? Locked,
        IReadOnlyList<StockLine> OtherStock,
        int FactoryCount,
        int ProblemCount,
        bool HasLastTurn);

    /// <summary>
    /// <paramref name="nodes"/> — список узлов сектора (<see cref="FactoryOverviewList.Build"/>);
    /// <paramref name="factoryNames"/> — отображаемые имена экземпляров («Рудник», «Рудник №2»);
    /// <paramref name="problemsByFactoryId"/> — короткие причины из «Требует внимания»;
    /// <paramref name="producedLastTurn"/>/<paramref name="consumedLastTurn"/> — выпуск и потребление
    /// каждого экземпляра в последнем расчёте (нет ключа — фабрика в нём не участвовала).
    /// </summary>
    public static Chain Build(
        IReadOnlyList<FactoryOverviewList.Node> nodes,
        IReadOnlyDictionary<Ulid, string> factoryNames,
        IReadOnlyDictionary<Ulid, IReadOnlyList<string>> problemsByFactoryId,
        IReadOnlyList<(Material Material, decimal Quantity)> stock,
        IReadOnlyDictionary<Ulid, decimal> producedLastTurn,
        IReadOnlyDictionary<Ulid, IReadOnlyDictionary<string, decimal>> consumedLastTurn,
        int unlockedGeneration)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(factoryNames);
        ArgumentNullException.ThrowIfNull(problemsByFactoryId);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(producedLastTurn);
        ArgumentNullException.ThrowIfNull(consumedLastTurn);

        var built = nodes.Where(node => node.Instance is not null).Select(node => node.Instance!).ToList();
        var factoryCount = built.Count;

        // Место материала в цепочке — передел того типа сектора, который его производит (любым
        // рецептом), а не номер уровня самого материала: у материала другого сектора может оказаться
        // тот же номер, и он встал бы на склад чужого передела.
        var producingLevelByMaterialId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var definition in nodes.Select(node => node.Definition).DistinctBy(definition => definition.Id))
        {
            foreach (var recipe in definition.Recipes)
            {
                producingLevelByMaterialId.TryAdd(recipe.Output.Id, LevelOf(definition));
            }
        }

        // Продукт своей фабрики показываем и при нулевом остатке: «+1 700 · −1 700» значит, что всё
        // произведённое сразу забрали, — это ответ, а не пустое место.
        var stockedIds = stock.Select(entry => entry.Material.Id).ToHashSet(StringComparer.Ordinal);
        var producedButAbsent = built
            .Select(factory => factory.SelectedRecipe.Output)
            .Where(material => !stockedIds.Contains(material.Id))
            .DistinctBy(material => material.Id)
            .Select(material => (Material: material, Quantity: 0m));

        var stockLines = stock
            .Concat(producedButAbsent)
            .OrderBy(entry => entry.Material.Level)
            .ThenBy(entry => entry.Material.Name, StringComparer.Ordinal)
            .Select(entry => BuildStockLine(entry.Material, entry.Quantity, built, factoryNames, producedLastTurn, consumedLastTurn))
            .ToList();

        var levels = new List<Level>();
        foreach (var levelGroup in nodes.GroupBy(node => LevelOf(node.Definition)).Where(group => group.Key <= unlockedGeneration).OrderBy(group => group.Key))
        {
            var types = levelGroup
                .GroupBy(node => node.Definition.Id)
                .Select(typeGroup => BuildTypeGroup(typeGroup.ToList(), factoryNames, problemsByFactoryId))
                .ToList();
            var problemCount = types.Sum(type => type.ProblemCount);
            var levelFactoryCount = types.Sum(type => type.Instances.Count);
            levels.Add(new Level(
                levelGroup.Key,
                types,
                stockLines.Where(line => producingLevelByMaterialId.TryGetValue(line.Material.Id, out var level) && level == levelGroup.Key).ToList(),
                levelFactoryCount,
                types.Sum(type => type.LastTurnOutput),
                problemCount,
                OpenByDefault: problemCount > 0 || factoryCount <= SmallChainFactoryCount));
        }

        var lockedNodes = nodes.Where(node => LevelOf(node.Definition) > unlockedGeneration).ToList();
        var locked = lockedNodes.Count == 0
            ? null
            : new LockedLevels(
                lockedNodes.Min(node => LevelOf(node.Definition)),
                lockedNodes.Max(node => LevelOf(node.Definition)),
                lockedNodes.Select(node => node.Definition.Name).Distinct().ToList());

        var otherStock = stockLines.Where(line => !producingLevelByMaterialId.ContainsKey(line.Material.Id)).ToList();

        return new Chain(
            levels, locked, otherStock, factoryCount, levels.Sum(level => level.ProblemCount),
            HasLastTurn: producedLastTurn.Count > 0 || consumedLastTurn.Count > 0);
    }

    /// <summary>Передел типа — по первому рецепту, как и в <see cref="FactoryOverviewList"/>: экземпляр со сменённым рецептом не должен убегать от своего типа.</summary>
    public static int LevelOf(FactoryDefinition definition) => definition.Recipes[0].Output.Level;

    private static TypeGroup BuildTypeGroup(
        IReadOnlyList<FactoryOverviewList.Node> nodes,
        IReadOnlyDictionary<Ulid, string> factoryNames,
        IReadOnlyDictionary<Ulid, IReadOnlyList<string>> problemsByFactoryId)
    {
        var instances = nodes
            .Where(node => node.Instance is not null)
            .Select(node =>
            {
                var factory = node.Instance!;
                return new Instance(
                    factory.Id,
                    factoryNames.GetValueOrDefault(factory.Id, factory.Definition.Name),
                    factory.Workers,
                    factory.Condition,
                    factory.IsUnderRepair,
                    node.LastTurnOutput,
                    node.TheoreticalMaxOutput ?? 0m,
                    problemsByFactoryId.GetValueOrDefault(factory.Id, Array.Empty<string>()));
            })
            .ToList();

        return new TypeGroup(
            nodes[0].Definition,
            instances,
            instances.Sum(instance => instance.Workers),
            instances.Sum(instance => instance.LastTurnOutput ?? 0m),
            instances.Sum(instance => instance.MaxOutput),
            instances.Count == 0 ? 1m : instances.Min(instance => instance.Condition),
            instances.Count(instance => instance.Problems.Count > 0));
    }

    private static StockLine BuildStockLine(
        Material material,
        decimal quantity,
        IReadOnlyList<Factory> built,
        IReadOnlyDictionary<Ulid, string> factoryNames,
        IReadOnlyDictionary<Ulid, decimal> producedLastTurn,
        IReadOnlyDictionary<Ulid, IReadOnlyDictionary<string, decimal>> consumedLastTurn)
    {
        var producers = built.Where(factory => factory.SelectedRecipe.Output.Id == material.Id).ToList();
        var consumers = built.Where(factory => factory.SelectedRecipe.Inputs.Any(input => input.Material.Id == material.Id)).ToList();

        // Потребление — по экземплярам, с их именами: «Рудник №2» и «Рудник» забирают сырьё по
        // отдельности, и игроку важно видеть, кто именно.
        var consumed = consumers
            .Select(factory => (
                Name: factoryNames.GetValueOrDefault(factory.Id, factory.Definition.Name),
                Quantity: consumedLastTurn.TryGetValue(factory.Id, out var inputs) ? inputs.GetValueOrDefault(material.Id) : 0m))
            .ToList();

        return new StockLine(
            material,
            quantity,
            producers.Sum(factory => producedLastTurn.GetValueOrDefault(factory.Id)),
            consumed,
            producers.Select(factory => factory.Definition.Name).Distinct().ToList(),
            consumers.Select(factory => factory.Definition.Name).Distinct().ToList());
    }
}
