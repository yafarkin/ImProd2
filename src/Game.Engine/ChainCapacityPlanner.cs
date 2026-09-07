using Game.Config.Economy;
using Game.Config.Loading;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Сколько мощности нужно каждому уровню цепочки, чтобы прокормить те уровни, что стоят над ним
/// (запрос пользователя, 2026-09-07: «я предполагал, что игроки будут R&amp;D вкачивать, нанимать
/// больше рабочих и потом строить новые фабрики на этом же уровне»). До этого и <see cref="SimpleBot"/>,
/// и <see cref="IdealHallCalculator"/> нанимали ровно <see cref="WorkerProductivityConfig.BaseWorkerCount"/>
/// один раз при постройке и строили не больше одной фабрики на пару (тип, рецепт) — то есть оба
/// физически не умели расшивать узкое место, и любой конфиг, задуманный с расчётом на рост мощности
/// по ходу партии, выглядел у стенда сломанным (см. <c>docs/economy-accounting-audit.md</c>, раздел
/// про дефициты <c>metallurgy.json</c>).
///
/// <para>
/// Считается сверху вниз: самый глубокий передел работает на базовой численности одной фабрикой, а
/// каждый уровень ниже подбирает численность (и, если её не хватает, число фабрик) под суммарную
/// потребность своих потребителей. Потребность — та же формула, что у §1d диагностики:
/// <c>Σ(Quantity_входа × выпуск_потребителя / OutputQuantity_потребителя)</c>, с обязательным делением
/// на <c>OutputQuantity</c> потребителя.
/// </para>
///
/// <para>
/// Донайм ограничен <see cref="MaxWorkersMultiplier"/> — не из-за денег (под наценкой «себестоимость +
/// %» зарплата входит в себестоимость, поэтому лишний рабочий формально всегда «выгоден», и без
/// ограничения бот нанимал бы бесконечно, эксплуатируя вырожденность ценообразования), а по смыслу:
/// нанимаем РОВНО под потребность, дальше расширяемся новой фабрикой. Сверх потребности не нанимаем
/// вовсе — у уровня без внутренних потребителей (конечный продукт) план остаётся базовым.
/// </para>
/// </summary>
public static class ChainCapacityPlanner
{
    /// <summary>Потолок донайма относительно базовой численности: дальше выгоднее и честнее ставить вторую фабрику, а не раздувать одну.</summary>
    public const int MaxWorkersMultiplier = 3;

    /// <summary>Сколько фабрик одной пары (тип, рецепт) план готов запросить — страховка от конфига, где узкое место нельзя расшить в принципе.</summary>
    public const int MaxFactoriesPerRecipe = 4;

    /// <summary>План по одной паре (тип фабрики, рецепт): сколько таких фабрик держать и по сколько рабочих на каждой.</summary>
    public sealed record RecipePlan
    {
        public required string FactoryDefinitionId { get; init; }
        public required string RecipeId { get; init; }
        public required string OutputMaterialId { get; init; }
        public required int Level { get; init; }

        /// <summary>Сколько фабрик этой пары нужно (минимум 1).</summary>
        public required int FactoryCount { get; init; }

        /// <summary>Сколько рабочих на каждой из них (не меньше базовой численности).</summary>
        public required int WorkersPerFactory { get; init; }

        /// <summary>Суммарный выпуск за ход при этом плане.</summary>
        public required decimal PlannedOutputPerTurn { get; init; }

        /// <summary>Сколько выпуска просят потребители внутри цепочки (0 — конечный продукт, потребителей нет).</summary>
        public required decimal DemandPerTurn { get; init; }

        /// <summary>План упёрся в потолки (<see cref="MaxWorkersMultiplier"/> × <see cref="MaxFactoriesPerRecipe"/>) и всё равно не закрывает потребность — узкое место не расшивается доступными рычагами вообще.</summary>
        public bool IsUnsatisfiable => DemandPerTurn > 0m && PlannedOutputPerTurn < DemandPerTurn;
    }

    /// <summary>
    /// Строит план по всему конфигу. Чистая функция: ни хода, ни рынка, ни денег — только граф
    /// рецептов и производительность. Деньги (может ли команда позволить себе этот план прямо сейчас)
    /// — забота вызывающего, см. <c>SimpleBot</c>.
    /// </summary>
    public static IReadOnlyDictionary<(string FactoryDefinitionId, string RecipeId), RecipePlan> Plan(ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var productivity = config.Raw.WorkerProductivity;
        var rnd = config.Raw.Rnd;
        var baseWorkers = productivity.BaseWorkerCount;

        var pairs = config.FactoryDefinitions
            .SelectMany(definition => definition.Recipes.Select(recipe => (Definition: definition, Recipe: recipe)))
            .ToList();

        // Потребность накапливается сверху вниз: пока считаем уровень L, все потребители его выхода
        // (они стоят выше по уровню) уже посчитаны и свою потребность объявили.
        var demandByMaterialId = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var plans = new Dictionary<(string, string), RecipePlan>();

        foreach (var (definition, recipe) in pairs.OrderByDescending(p => p.Recipe.Output.Level)
                     .ThenBy(p => p.Definition.Id, StringComparer.Ordinal)
                     .ThenBy(p => p.Recipe.Id, StringComparer.Ordinal))
        {
            var demand = demandByMaterialId.GetValueOrDefault(recipe.Output.Id);
            var baseOutput = OutputAt(definition, recipe, baseWorkers, productivity, rnd);

            var factoryCount = 1;
            var workers = baseWorkers;
            var output = baseOutput;

            // Сначала донайм (расход только на зарплату), и лишь когда упёрлись в потолок численности —
            // ещё одна фабрика: у неё, кроме зарплаты, есть ещё и BuildCost с содержанием.
            while (output < demand && (workers < baseWorkers * MaxWorkersMultiplier || factoryCount < MaxFactoriesPerRecipe))
            {
                if (workers < baseWorkers * MaxWorkersMultiplier)
                {
                    workers = WorkersForOutput(definition, recipe, demand / factoryCount, productivity, rnd, baseWorkers);
                }
                else
                {
                    factoryCount++;
                    workers = WorkersForOutput(definition, recipe, demand / factoryCount, productivity, rnd, baseWorkers);
                }

                output = OutputAt(definition, recipe, workers, productivity, rnd) * factoryCount;
            }

            plans[(definition.Id, recipe.Id)] = new RecipePlan
            {
                FactoryDefinitionId = definition.Id,
                RecipeId = recipe.Id,
                OutputMaterialId = recipe.Output.Id,
                Level = recipe.Output.Level,
                FactoryCount = factoryCount,
                WorkersPerFactory = workers,
                PlannedOutputPerTurn = output,
                DemandPerTurn = demand,
            };

            if (recipe.OutputQuantity <= 0m)
            {
                continue;
            }

            var batches = output / recipe.OutputQuantity;
            foreach (var input in recipe.Inputs)
            {
                demandByMaterialId.TryGetValue(input.Material.Id, out var existing);
                demandByMaterialId[input.Material.Id] = existing + input.Quantity * batches;
            }
        }

        return plans;
    }

    /// <summary>Выпуск пары (тип, рецепт) за ход при заданной численности — фабрика первого уровня, состояние 1.0 (тот же срез, что у всех статических расчётов).</summary>
    private static decimal OutputAt(
        FactoryDefinition definition, Recipe recipe, int workers, WorkerProductivityConfig productivity, RndConfig rnd)
    {
        var factory = new Factory(Ulid.NewUlid(), definition.Sector, definition, recipe);
        factory.Hire(workers);
        return ProductionCalculator.CalculateCapacityBreakdown(factory, productivity, rnd).TheoreticalMaxOutput;
    }

    /// <summary>
    /// Наименьшая численность, при которой одна фабрика выдаёт <paramref name="targetOutput"/> — не меньше
    /// базовой и не больше потолка донайма. Считается перебором по одному рабочему, а не обращением
    /// формулы: диапазон крошечный (десятки), зато не нужно повторять здесь кусочно-линейную кривую
    /// убывающей отдачи из <see cref="ProductionCalculator"/> и рисковать разойтись с ней в будущем.
    /// </summary>
    private static int WorkersForOutput(
        FactoryDefinition definition, Recipe recipe, decimal targetOutput,
        WorkerProductivityConfig productivity, RndConfig rnd, int baseWorkers)
    {
        var maxWorkers = baseWorkers * MaxWorkersMultiplier;
        for (var workers = baseWorkers; workers < maxWorkers; workers++)
        {
            if (OutputAt(definition, recipe, workers, productivity, rnd) >= targetOutput)
            {
                return workers;
            }
        }

        return maxWorkers;
    }
}
