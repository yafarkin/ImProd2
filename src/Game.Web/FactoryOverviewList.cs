using Game.Domain;

namespace Game.Web;

/// <summary>
/// Единый список фабрик сектора команды для /team — построенное и то, что ещё можно построить,
/// вместе (запрос пользователя: «слева направо по уровню, построенное и то, что можно построить —
/// вместе»), отсортированный по уровню сырьевой пирамиды и выводимый как вертикальный список карточек
/// (запрос пользователя: раньше это была горизонтальная SVG-диаграмма колонками, но на телефоне
/// горизонтальный layout неудобен — вертикальный список одинаково хорошо работает и на телефоне, и на
/// большом экране). Склад команды общий на все фабрики (см. <see cref="Warehouse"/>), поэтому стрелок
/// «кто что потребляет» здесь нет — вместо них у построенного узла есть <see cref="Node.Status"/>:
/// хватило ли ему в прошлый ход сырья, чтобы выйти на полную мощность.
/// </summary>
public static class FactoryOverviewList
{
    /// <summary>
    /// Состояние узла: <see cref="NotBuilt"/> — «построить» (есть всегда, для каждого типа сектора,
    /// независимо от того, сколько экземпляров уже построено); <see cref="Adequate"/> — построенному
    /// экземпляру в прошлый ход хватило сырья на полную мощность (или ходов ещё не было — тогда
    /// тревогу не поднимаем); <see cref="ShortOfInput"/> — фактический выпуск в прошлый ход был
    /// меньше того, что позволили бы рабочие и уровень, из-за нехватки сырья на общем складе.
    /// </summary>
    public enum LoadStatus { NotBuilt, Adequate, ShortOfInput }

    /// <summary>
    /// Один узел списка. <see cref="Instance"/> равен <c>null</c> для узла-плейсхолдера «построить
    /// ещё» — тогда <see cref="IndexWithinType"/>, <see cref="LastTurnOutput"/> и
    /// <see cref="TheoreticalMaxOutput"/> тоже не заданы. Для построенного экземпляра
    /// <see cref="IndexWithinType"/> — порядковый номер среди экземпляров того же типа (1, 2, ...);
    /// <see cref="LastTurnOutput"/> — сколько реально произведено в прошлый завершённый ход
    /// (запрос пользователя: реальный факт вместо оценки прибыли по рыночным ценам — она путала,
    /// не давая понять, что вообще делать с числом), <c>null</c>, если ходов ещё не было;
    /// <see cref="TheoreticalMaxOutput"/> — потолок выпуска по рабочим/уровню/рецепту без учёта
    /// нехватки сырья (та же величина, что и на вкладке карточки «Прибыльность»), для сравнения с
    /// фактом и подписи «не хватило N до максимума».
    /// </summary>
    public sealed record Node(
        FactoryDefinition Definition, Factory? Instance, int? IndexWithinType,
        LoadStatus Status, decimal? LastTurnOutput, decimal? TheoreticalMaxOutput);

    /// <summary>
    /// <paramref name="sectorDefinitions"/> — все типы фабрик сектора команды (построенные и ещё
    /// нет); <paramref name="builtFactories"/> — фактически построенные фабрики этой команды (может
    /// быть несколько экземпляров одного типа); <paramref name="lastTurnOutputByFactoryId"/> и
    /// <paramref name="theoreticalMaxOutputByFactoryId"/> — заранее посчитанные вызывающей стороной
    /// факт и потолок выпуска каждого экземпляра (реплей журнала и
    /// <see cref="Game.Engine.ProductionCalculator.CalculateCapacityBreakdown"/> нужны конфигу
    /// сессии, которого у этого класса нет) — отсутствие ключа в первом словаре значит «ходов ещё не
    /// было», не «выпуск нулевой».
    /// </summary>
    public static IReadOnlyList<Node> Build(
        IReadOnlyList<FactoryDefinition> sectorDefinitions,
        IReadOnlyList<Factory> builtFactories,
        IReadOnlyDictionary<Ulid, decimal> lastTurnOutputByFactoryId,
        IReadOnlyDictionary<Ulid, decimal> theoreticalMaxOutputByFactoryId)
    {
        ArgumentNullException.ThrowIfNull(sectorDefinitions);
        ArgumentNullException.ThrowIfNull(builtFactories);
        ArgumentNullException.ThrowIfNull(lastTurnOutputByFactoryId);
        ArgumentNullException.ThrowIfNull(theoreticalMaxOutputByFactoryId);

        var builtByDefinitionId = builtFactories
            .GroupBy(factory => factory.Definition.Id)
            .ToDictionary(group => group.Key, group => group.OrderBy(factory => factory.Id).ToList());

        // Позиция типа по уровню считается по первому рецепту типа (как раньше в FactoryChainDiagram)
        // — построенные экземпляры этого типа могут выбрать другой рецепт, но и они, и плейсхолдер
        // «построить ещё» обязаны оказаться в одной группе, иначе плейсхолдер «убегает» от своих же
        // построенных экземпляров при смене ими рецепта.
        var byLevel = sectorDefinitions
            .GroupBy(definition => definition.Recipes[0].Output.Level)
            .OrderBy(group => group.Key);

        var nodes = new List<Node>();

        foreach (var levelGroup in byLevel)
        {
            foreach (var definition in levelGroup.OrderBy(definition => definition.Name, StringComparer.Ordinal))
            {
                var built = builtByDefinitionId.GetValueOrDefault(definition.Id, []);
                var index = 1;
                foreach (var instance in built)
                {
                    var (status, lastTurnOutput) = ResolveStatus(instance, lastTurnOutputByFactoryId, theoreticalMaxOutputByFactoryId);
                    var theoreticalMax = theoreticalMaxOutputByFactoryId.GetValueOrDefault(instance.Id);
                    nodes.Add(new Node(definition, instance, index, status, lastTurnOutput, theoreticalMax));
                    index++;
                }

                // Плейсхолдер «построить ещё» — ровно один на тип, даже если экземпляры уже есть
                // (запрос пользователя: «он должен быть, даже если фабрики уже построены»).
                nodes.Add(new Node(definition, null, null, LoadStatus.NotBuilt, null, null));
            }
        }

        return nodes;
    }

    private static (LoadStatus Status, decimal? LastTurnOutput) ResolveStatus(
        Factory factory,
        IReadOnlyDictionary<Ulid, decimal> lastTurnOutputByFactoryId,
        IReadOnlyDictionary<Ulid, decimal> theoreticalMaxOutputByFactoryId)
    {
        if (!lastTurnOutputByFactoryId.TryGetValue(factory.Id, out var lastTurnOutput))
        {
            return (LoadStatus.Adequate, null);
        }

        var theoreticalMax = theoreticalMaxOutputByFactoryId.GetValueOrDefault(factory.Id);
        var status = lastTurnOutput < theoreticalMax ? LoadStatus.ShortOfInput : LoadStatus.Adequate;

        return (status, lastTurnOutput);
    }
}
