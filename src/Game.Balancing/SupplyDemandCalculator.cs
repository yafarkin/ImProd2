using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Физический баланс спроса и предложения по каждому материалу — §1d диагностики (в плане исследований
/// <c>docs/rebalance-2sector/balance-experiment-plan.md</c> это «итерация 2, §5»; печатается среди
/// статических проверок, потому что тоже не требует ни хода, ни рынка, ни бота). Отвечает на вопрос,
/// который ни одна другая секция не задаёт вовсе: **хватит ли выпуска материала на то, что просят у
/// него соседи по рецепту**.
///
/// <para>
/// Зачем отдельная проверка. §1b считает окупаемость уровня при загрузке 100% — а уровень, которому
/// хронически недостаёт сырья, работает на долю u от мощности, и окупаемость растягивается в 1/u раз,
/// оставаясь при этом формально «зелёной». Проверено на живом примере (2026-09-07,
/// <c>docs/economy-accounting-audit.md</c>, раздел «дешёвая гипотеза»): у <c>metallurgy.json</c>
/// срезание BuildCost сделало §1b полностью чистым, а Score(90) реального бота при этом стал ХУЖЕ
/// (−34 750 → −43 712) — дешёвые фабрики позволили построить больше мощности, которую нечем кормить,
/// и каждая недокормленная фабрика продолжала платить содержание и зарплату каждый ход. Виноваты были
/// ровно два материала с дефицитом 0.62×, которых не видел ни один инструмент.
/// </para>
///
/// <para>
/// Формула спроса — та же, на которой раньше сами себя ловили дважды (<c>fastener-plant</c> 2026-08-23,
/// <c>wire-rod</c> 2026-08-24): делить на <see cref="Recipe.OutputQuantity"/> обязательно, иначе рецепт
/// вида «1 пруток → 20 крепежей» считается потребляющим в 20 раз больше, чем на самом деле:
/// <c>спрос(m) = Σ(Quantity_входа × выпуск_потребителя / OutputQuantity_потребителя)</c>.
/// </para>
///
/// <para>
/// Число команд в расчёте не участвует: и спрос, и предложение растут пропорционально числу команд,
/// отношение не меняется — при условии, что команд в каждом секторе поровну (так настроены все наши
/// прогоны). Единица агрегации — пара (тип фабрики, рецепт), а не тип: и <see cref="SimpleBot"/>, и
/// <see cref="IdealHallCalculator"/> строят отдельную фабрику на каждый рецепт многорецептного типа,
/// значит все рецепты типа работают одновременно.
/// </para>
/// </summary>
public static class SupplyDemandCalculator
{
    /// <summary>Порог предупреждения: запас меньше этого — дефицита формально нет, но нет и запаса на износ/простой/раскачку.</summary>
    public const decimal ThinSlackRatio = 1.05m;

    /// <summary>Баланс одного материала: сколько его производится за ход против того, сколько просят соседи по рецепту.</summary>
    public sealed record MaterialBalance
    {
        public required string MaterialId { get; init; }
        public required string SectorId { get; init; }
        public required int Level { get; init; }

        /// <summary>Суммарный теоретический выпуск за ход по всем парам (фабрика, рецепт), производящим этот материал, при <c>workersPerFactory</c> рабочих.</summary>
        public required decimal SupplyPerTurn { get; init; }

        /// <summary>Суммарная потребность за ход всех рецептов, у которых этот материал стоит входом (с делением на <see cref="Recipe.OutputQuantity"/> потребителя).</summary>
        public required decimal DemandPerTurn { get; init; }

        /// <summary>Рецепты-потребители — чтобы сразу видеть, кому именно не хватает.</summary>
        public required IReadOnlyList<string> ConsumerRecipeIds { get; init; }

        /// <summary>Отношение предложения к спросу; <c>null</c> — материал никто не потребляет (конечный продукт), балансировать нечего.</summary>
        public decimal? Ratio => DemandPerTurn > 0m ? SupplyPerTurn / DemandPerTurn : null;

        /// <summary>Дефицит — соседям физически не хватает этого материала, сколько бы денег у команды ни было.</summary>
        public bool IsDeficit => Ratio is { } ratio && ratio < 1m;

        /// <summary>Дефицита нет, но запас настолько тонкий, что любой простой/износ уводит уровень ниже единицы.</summary>
        public bool IsThinSlack => Ratio is { } ratio && ratio >= 1m && ratio < ThinSlackRatio;
    }

    /// <summary>
    /// Считает баланс по каждому материалу конфига. Чистая функция: ни рынка, ни хода, ни бота —
    /// одна свёртка по графу рецептов.
    /// </summary>
    public static IReadOnlyList<MaterialBalance> Calculate(ResolvedGameConfig config, int workersPerFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (workersPerFactory <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workersPerFactory), workersPerFactory, "Worker count must be positive.");
        }

        var productivity = config.Raw.WorkerProductivity;
        var rnd = config.Raw.Rnd;

        var supplyByMaterialId = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var demandByMaterialId = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var consumersByMaterialId = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var factoryDef in config.FactoryDefinitions)
        {
            foreach (var recipe in factoryDef.Recipes)
            {
                var factory = new Factory(Ulid.NewUlid(), factoryDef.Sector, factoryDef, recipe);
                factory.Hire(workersPerFactory);
                var outputQuantity = ProductionCalculator.CalculateCapacityBreakdown(factory, productivity, rnd).TheoreticalMaxOutput;

                supplyByMaterialId.TryGetValue(recipe.Output.Id, out var supply);
                supplyByMaterialId[recipe.Output.Id] = supply + outputQuantity;

                if (recipe.OutputQuantity <= 0m)
                {
                    continue;
                }

                var batches = outputQuantity / recipe.OutputQuantity;
                foreach (var input in recipe.Inputs)
                {
                    demandByMaterialId.TryGetValue(input.Material.Id, out var demand);
                    demandByMaterialId[input.Material.Id] = demand + input.Quantity * batches;

                    if (!consumersByMaterialId.TryGetValue(input.Material.Id, out var consumers))
                    {
                        consumers = new List<string>();
                        consumersByMaterialId[input.Material.Id] = consumers;
                    }
                    consumers.Add(recipe.Id);
                }
            }
        }

        return config.Materials.Values
            .OrderBy(material => material.Sector.Id, StringComparer.Ordinal)
            .ThenBy(material => material.Level)
            .ThenBy(material => material.Id, StringComparer.Ordinal)
            .Select(material => new MaterialBalance
            {
                MaterialId = material.Id,
                SectorId = material.Sector.Id,
                Level = material.Level,
                SupplyPerTurn = supplyByMaterialId.GetValueOrDefault(material.Id),
                DemandPerTurn = demandByMaterialId.GetValueOrDefault(material.Id),
                ConsumerRecipeIds = consumersByMaterialId.TryGetValue(material.Id, out var consumers)
                    ? consumers.OrderBy(id => id, StringComparer.Ordinal).ToList()
                    : Array.Empty<string>(),
            })
            .ToList();
    }

    /// <summary>
    /// Строки по материалам, чей разрыв НЕ закрывается доступными рычагами (<see cref="ChainCapacityPlanner"/>
    /// упёрся в свои потолки — донайм до <see cref="ChainCapacityPlanner.MaxWorkersMultiplier"/>× базовой
    /// численности и до <see cref="ChainCapacityPlanner.MaxFactoriesPerRecipe"/> фабрик на пару). Пусто —
    /// значит либо дефицита нет вовсе, либо он закрывается тем, что живая команда и так умеет делать
    /// (запрос пользователя 2026-09-07: расширение мощности — это решение игрока, а не ошибка конфига;
    /// см. <see cref="FormatPlannedExpansion"/>).
    /// </summary>
    public static IReadOnlyList<string> FormatDeficits(
        IReadOnlyList<MaterialBalance> balances,
        IReadOnlyDictionary<(string FactoryDefinitionId, string RecipeId), ChainCapacityPlanner.RecipePlan> capacityPlan)
    {
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(capacityPlan);

        var unsatisfiableMaterialIds = capacityPlan.Values
            .Where(plan => plan.IsUnsatisfiable)
            .Select(plan => plan.OutputMaterialId)
            .ToHashSet(StringComparer.Ordinal);

        return balances
            .Where(balance => balance.IsDeficit && unsatisfiableMaterialIds.Contains(balance.MaterialId))
            .OrderBy(balance => balance.Ratio)
            .Select(balance =>
                $"{balance.SectorId}, уровень {balance.Level}, {balance.MaterialId}: выпуск {balance.SupplyPerTurn:F0}/ход " +
                $"против спроса {balance.DemandPerTurn:F0}/ход = {balance.Ratio:F2}× — нехватка {1m - balance.Ratio!.Value:P0}, " +
                $"и она НЕ закрывается ни доньмом (потолок {ChainCapacityPlanner.MaxWorkersMultiplier}× базовой численности), " +
                $"ни новыми фабриками (потолок {ChainCapacityPlanner.MaxFactoriesPerRecipe} на рецепт). " +
                $"Потребители: {string.Join(", ", balance.ConsumerRecipeIds)}. " +
                "Поднять ProductionRate производителей или снизить потребность/ProductionRate потребителей.")
            .ToList();
    }

    /// <summary>
    /// Строки по уровням, которым план предписывает расширение (донайм и/или вторая фабрика) — это НЕ
    /// проблема, а ровно то решение, ради которого дефицит в конфиге и оставляют: показываем цену
    /// вопроса, чтобы было видно, во что игроку обойдётся расшивка узкого места.
    /// </summary>
    public static IReadOnlyList<string> FormatPlannedExpansion(
        IReadOnlyDictionary<(string FactoryDefinitionId, string RecipeId), ChainCapacityPlanner.RecipePlan> capacityPlan,
        int baseWorkerCount)
    {
        ArgumentNullException.ThrowIfNull(capacityPlan);

        return capacityPlan.Values
            .Where(plan => !plan.IsUnsatisfiable && (plan.FactoryCount > 1 || plan.WorkersPerFactory > baseWorkerCount))
            .OrderBy(plan => plan.Level)
            .ThenBy(plan => plan.OutputMaterialId, StringComparer.Ordinal)
            .Select(plan =>
                $"уровень {plan.Level}, {plan.OutputMaterialId} ({plan.FactoryDefinitionId}/{plan.RecipeId}): " +
                $"под спрос {plan.DemandPerTurn:F0}/ход нужно {plan.FactoryCount} фабрик(и) по {plan.WorkersPerFactory} рабочих " +
                $"(базовая численность {baseWorkerCount}) — расширяется доступными рычагами, это решение игрока, не поломка конфига.")
            .ToList();
    }

    /// <summary>Строки по материалам с тонким (меньше <see cref="ThinSlackRatio"/>) запасом — не блокируют вердикт, но стоит знать.</summary>
    public static IReadOnlyList<string> FormatThinSlack(IReadOnlyList<MaterialBalance> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);

        return balances
            .Where(balance => balance.IsThinSlack)
            .OrderBy(balance => balance.Ratio)
            .Select(balance =>
                $"{balance.SectorId}, уровень {balance.Level}, {balance.MaterialId}: запас всего {balance.Ratio:F2}× — " +
                "дефицита нет, но любой простой/износ уводит потребителей ниже мощности.")
            .ToList();
    }
}
