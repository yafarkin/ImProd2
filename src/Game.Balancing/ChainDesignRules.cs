using Game.Config.Loading;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Правила дизайна цепочки, которые до 2026-09-07 жили только текстом в
/// <c>docs/production-chain-calibration-lessons.md</c> и потому проверялись глазами — то есть иногда
/// не проверялись вовсе (запрос пользователя: инструмент должен содержать весь накопленный опыт, а не
/// отсылать к памятке). Каждое из четырёх правил хотя бы раз было нарушено на живом файле и стоило
/// отдельного расследования.
///
/// <para>
/// Секция §0 диагностики: считается за миллисекунды обходом конфига, до всякой арифметики
/// себестоимости, и отвечает не на «сходятся ли числа», а на «правильной ли формы цепочка».
/// Нарушение здесь не блокирует вердикт само по себе — оно объясняет, ПОЧЕМУ не сойдутся числа
/// ниже, и почти всегда чинится раньше и дешевле, чем подбор коэффициентов.
/// </para>
/// </summary>
public static class ChainDesignRules
{
    /// <summary>
    /// Во сколько раз суммарный вход рецепта должен превысить медиану по своему уровню, чтобы это
    /// сочли подозрительным. Аддитивный кросс-вход (запрещённый паттерн §1 памятки) удваивает вход,
    /// поэтому порог заметно ниже двойки — но и заметно выше единицы, чтобы честная разница в
    /// рецептуре между соседями не поднимала ложную тревогу.
    /// </summary>
    public const decimal AdditiveCrossInputRatio = 1.5m;

    /// <summary>Доля партии, к которой последний уровень обязан быть разблокирован: иначе ему просто негде окупиться (правило §3 памятки, выведено на <c>metallurgy.json</c> — уровень 9 открывался к 66-му ходу из 90).</summary>
    public const decimal LastLevelUnlockShareOfGame = 0.25m;

    /// <summary>Одна найденная проблема дизайна: что не так и что с этим делать.</summary>
    public sealed record Finding(string Rule, string Message);

    public static IReadOnlyList<Finding> Check(ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var findings = new List<Finding>();
        findings.AddRange(CheckAdditiveCrossInputs(config));
        findings.AddRange(CheckLastLevelUnlockTurn(config));
        findings.AddRange(CheckTotalBuildCostAgainstBudget(config));
        findings.AddRange(CheckThroughputAgainstFreeWarehouse(config));
        return findings;
    }

    /// <summary>
    /// Правило §1 памятки, самая дорогая ошибка за всю калибровку: кросс-секторный вход обязан ДЕЛИТЬ
    /// количество со своим сырьём (0.4 кокса + 0.1 пиломатериала), а не добавляться сверху (0.5 + 0.5).
    /// Аддитивная связь молча удваивает стоимость сырья без какой-либо компенсации — рецепт становится
    /// убыточным даже при идеальной игре, и никакая проверка себестоимости этого не видит, потому что
    /// себестоимость по-прежнему монотонно растёт с уровнем.
    ///
    /// <para>
    /// Точного признака у этой ошибки нет — «сколько единиц входа на единицу выхода правильно» решает
    /// автор цепочки. Поэтому здесь эвристика: сравниваем суммарный вход рецепта с медианой по его же
    /// уровню. Рецепт, который просит в полтора раза больше соседей по уровню, — кандидат на ручную
    /// проверку, а не приговор; формулировка это и говорит.
    /// </para>
    /// </summary>
    private static IEnumerable<Finding> CheckAdditiveCrossInputs(ResolvedGameConfig config)
    {
        var recipesByLevel = config.Materials.Values
            .Select(material => (Material: material, Recipe: config.RecipeBook.TryGetRecipe(material)))
            .Where(pair => pair.Recipe is { Inputs.Count: > 0 } && pair.Recipe.OutputQuantity > 0m)
            .GroupBy(pair => pair.Material.Level);

        foreach (var levelGroup in recipesByLevel.OrderBy(group => group.Key))
        {
            var perUnitInput = levelGroup
                .Select(pair => (pair.Material, pair.Recipe, Total: pair.Recipe!.Inputs.Sum(i => i.Quantity) / pair.Recipe.OutputQuantity))
                .OrderBy(entry => entry.Total)
                .ToList();
            if (perUnitInput.Count < 3)
            {
                continue;
            }

            var median = perUnitInput[perUnitInput.Count / 2].Total;
            if (median <= 0m)
            {
                continue;
            }

            foreach (var entry in perUnitInput.Where(e => e.Total > median * AdditiveCrossInputRatio))
            {
                var imported = entry.Recipe!.Inputs
                    .Where(input => input.Material.Sector != entry.Material.Sector)
                    .Select(input => input.Material.Id)
                    .ToList();
                if (imported.Count == 0)
                {
                    continue;
                }

                yield return new Finding(
                    "аддитивный кросс-вход",
                    $"{entry.Material.SectorId()}, уровень {entry.Material.Level}, {entry.Recipe.Id}: суммарный вход " +
                    $"{entry.Total:F2} ед. на единицу выпуска против медианы {median:F2} по этому уровню " +
                    $"({entry.Total / median:F2}×), и среди входов есть импортные ({string.Join(", ", imported)}). " +
                    "Похоже на аддитивную кросс-связь: импортный вход должен ДЕЛИТЬ количество со своим сырьём " +
                    "(0.4 своего + 0.1 чужого), а не добавляться сверху к неизменному своему — иначе стоимость сырья " +
                    "удваивается без компенсации. Проверьте рецепт вручную: если так и задумано, это ложная тревога.");
            }
        }
    }

    /// <summary>
    /// Правило §3 памятки: последний уровень обязан открыться в первой четверти партии, иначе ему
    /// физически негде окупиться. Ход разблокировки считается при эталонном темпе идеального зала —
    /// вложения по потолку каждый ход; живая команда дойдёт позже, значит проверка оптимистична, и
    /// нарушение здесь означает, что в реальной игре будет только хуже.
    /// </summary>
    private static IEnumerable<Finding> CheckLastLevelUnlockTurn(ResolvedGameConfig config)
    {
        var research = config.Raw.GenerationResearch;
        var maxLevel = config.Materials.Values.Max(material => material.Level);
        if (research.StartingGeneration >= maxLevel || research.MaxCommitmentPerTurn <= 0m)
        {
            yield break;
        }

        var deadline = (int)Math.Ceiling(config.Raw.Duration.MaxTurns * LastLevelUnlockShareOfGame);
        var unlockTurn = 0;
        for (var turn = 1; turn <= config.Raw.Duration.MaxTurns; turn++)
        {
            var generation = GenerationResearchCalculator.CalculateResultingGeneration(
                research.StartingGeneration, turn * research.MaxCommitmentPerTurn, research);
            if (generation >= maxLevel)
            {
                unlockTurn = turn;
                break;
            }
        }

        if (unlockTurn == 0)
        {
            yield return new Finding(
                "поздняя разблокировка",
                $"Последний уровень ({maxLevel}) не разблокируется за всю партию ({config.Raw.Duration.MaxTurns} ходов) " +
                "даже при вложениях по потолку каждый ход. → Снизить ResearchPointThresholdsByGeneration или поднять " +
                "GenerationResearch.MaxCommitmentPerTurn.");
        }
        else if (unlockTurn > deadline)
        {
            yield return new Finding(
                "поздняя разблокировка",
                $"Последний уровень ({maxLevel}) открывается на ходу {unlockTurn} из {config.Raw.Duration.MaxTurns} — " +
                $"позже четверти партии (ход {deadline}), и это при вложениях по потолку каждый ход, живая команда " +
                "дойдёт ещё позже. Верхнему переделу негде окупиться. → Снизить последний порог " +
                $"ResearchPointThresholdsByGeneration либо поднять MaxCommitmentPerTurn примерно в " +
                $"{(decimal)unlockTurn / deadline:F2}×.");
        }
    }

    /// <summary>
    /// Правило §5 памятки, найдено на <c>metallurgy-7</c>: потолок отрицательного баланса обязан быть
    /// не меньше суммарных капзатрат цепочки. Иначе команда физически не может достроить её до конца —
    /// верхние уровни ждут выручки нижних и попадают в партию слишком поздно, чтобы окупиться.
    /// </summary>
    private static IEnumerable<Finding> CheckTotalBuildCostAgainstBudget(ResolvedGameConfig config)
    {
        var budget = config.Raw.StartingConditions.MaxInitialBuildBudget;
        if (budget <= 0m)
        {
            yield break;
        }

        var plan = ChainCapacityPlanner.Plan(config);
        var buildCostById = config.Raw.FactoryDefinitions.ToDictionary(definition => definition.Id, definition => definition.BuildCost);

        foreach (var sector in config.Sectors)
        {
            var sectorTotal = plan.Values
                .Where(recipePlan => config.FactoryDefinitions.First(d => d.Id == recipePlan.FactoryDefinitionId).Sector == sector)
                .Sum(recipePlan => recipePlan.FactoryCount * buildCostById[recipePlan.FactoryDefinitionId]);

            if (sectorTotal > budget)
            {
                yield return new Finding(
                    "капзатраты выше потолка минуса",
                    $"{sector.Id}: полная стройка стоит {sectorTotal:F0}, а потолок отрицательного баланса — {budget:F0} " +
                    $"({sectorTotal / budget:F2}× сверх). Команда не сможет достроить цепочку, верхние уровни попадут в " +
                    $"партию слишком поздно. → Поднять StartingConditions.MaxInitialBuildBudget минимум до {sectorTotal:F0} " +
                    "(с запасом) либо снизить BuildCost по цепочке.");
            }
        }
    }

    /// <summary>
    /// Правило §5 памятки, вторая половина: оборот цепочки за ход должен укладываться в бесплатный
    /// лимит склада. Сбор за превышение — фиксированная сумма за единицу, поэтому на дешёвом сырье он
    /// легко оказывается дороже самого товара (на <c>metallurgy.json</c> — в 4.6 раза), и цепочка
    /// уходит в минус по причине, которая к производству отношения не имеет.
    /// </summary>
    private static IEnumerable<Finding> CheckThroughputAgainstFreeWarehouse(ResolvedGameConfig config)
    {
        var freeCapacity = config.Raw.Warehouse.FreeCapacity;
        if (freeCapacity <= 0m)
        {
            yield break;
        }

        var plan = ChainCapacityPlanner.Plan(config);
        foreach (var sector in config.Sectors)
        {
            var throughput = plan.Values
                .Where(recipePlan => config.FactoryDefinitions.First(d => d.Id == recipePlan.FactoryDefinitionId).Sector == sector)
                .Sum(recipePlan => recipePlan.PlannedOutputPerTurn);

            if (throughput > freeCapacity)
            {
                yield return new Finding(
                    "оборот выше бесплатного склада",
                    $"{sector.Id}: цепочка гоняет {throughput:F0} ед./ход при бесплатном лимите склада {freeCapacity:F0} " +
                    $"({throughput / freeCapacity:F2}× сверх). Сбор за превышение — {config.Raw.Warehouse.OverageFeePerUnit} ¤ " +
                    "за единицу за ход, на дешёвом сырье это дороже самого товара. → Поднять Warehouse.FreeCapacity " +
                    $"минимум до {throughput:F0} либо уменьшить ProductionRate по цепочке.");
            }
        }
    }

    private static string SectorId(this Game.Domain.Material material) => material.Sector.Id;
}
