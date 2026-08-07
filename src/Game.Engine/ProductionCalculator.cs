using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Расчёт производства фабрик команды за один тик (SPEC §4 — «производство снизу вверх по уровням»
/// делает Блок 4.4, здесь — сам расчёт; SPEC §5.6 — производительность от рабочих). Чистая функция:
/// не трогает журнал и не мутирует состояние — результат оборачивается в <see cref="FactoryProduced"/>
/// и применяется вызывающим кодом через событие.
/// </summary>
public static class ProductionCalculator
{
    /// <summary>
    /// Разбивка теоретического потолка выпуска фабрики без учёта сырья (запрос пользователя «понять
    /// максимальную теоретическую производительность» — сколько дают рабочие, сколько добавляет
    /// R&amp;D) — те же слагаемые, что <see cref="CalculateGroup"/> считает внутри себя для
    /// <see cref="ProductionResult.CapacityLimitedOutputQuantity"/>, только не спрятанные в одно
    /// число, а показанные по отдельности для интерфейса.
    /// </summary>
    public sealed record CapacityBreakdown(
        int Workers,
        decimal EffectiveCapacity,
        int Level,
        decimal LevelBonus,
        decimal RecipeProductionRate,
        decimal Condition,
        bool IsUnderRepair,
        decimal TheoreticalMaxOutput);

    /// <summary>
    /// Считает <see cref="CapacityBreakdown"/> для фабрики по её текущему числу рабочих, уровню и
    /// выбранному рецепту — то же самое произведение, что даёт <c>CapacityLimitedOutputQuantity</c>
    /// в <see cref="CalculateGroup"/>, но с промежуточными слагаемыми напоказ. Не зависит от склада —
    /// это потолок «если бы сырья было бесконечно много», а не прогноз фактического выпуска.
    /// </summary>
    public static CapacityBreakdown CalculateCapacityBreakdown(Factory factory, WorkerProductivityConfig productivity, RndConfig rnd)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(productivity);
        ArgumentNullException.ThrowIfNull(rnd);

        var effectiveCapacity = CalculateEffectiveCapacity(factory.Workers, productivity);
        var levelBonus = 1m + (factory.Level - 1) * rnd.ProductionRateBonusPerLevel;
        var recipeRate = factory.SelectedRecipe.ProductionRate;
        // На простое (SPEC §5.6, вынужденном или по капремонту) множитель — RepairOutputMultiplier
        // (зафиксирован при начале именно этого простоя), не Condition — тот же приоритет, что в
        // CalculateGroup.
        var theoreticalMaxOutput = recipeRate * levelBonus * effectiveCapacity
                                    * (factory.IsUnderRepair ? factory.RepairOutputMultiplier : factory.Condition);

        return new CapacityBreakdown(factory.Workers, effectiveCapacity, factory.Level, levelBonus, recipeRate, factory.Condition, factory.IsUnderRepair, theoreticalMaxOutput);
    }

    /// <summary>Считает производство одной фабрики без конкуренции за сырьё с другими — тонкая обёртка над <see cref="CalculateGroup"/> для группы из одной фабрики.</summary>
    public static ProductionResult Calculate(Factory factory, Warehouse warehouse, WorkerProductivityConfig productivity, RndConfig rnd)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return CalculateGroup(new[] { factory }, warehouse, productivity, rnd).Single();
    }

    /// <summary>
    /// Считает, сколько произведёт за тик каждая фабрика из группы, деля один и тот же склад
    /// (обычно — все фабрики одной команды одного уровня материала, см. <see cref="GameSession.RunTick"/>):
    /// мощность от числа рабочих (линейно до базовой численности, дальше — с убывающей отдачей,
    /// SPEC §5.6) ограничивает выпуск сверху; фактически доступное на складе сырьё может ограничить
    /// его ещё сильнее. Если сырья хватает на желаемое сразу всем фабрикам группы — конкуренции нет,
    /// каждая получает сколько запросила. Если не хватает — дефицитный материал делится между
    /// претендентами пропорционально их <see cref="Factory.AllocationShare"/> (вес, не обязанный
    /// суммироваться до 100 — 60 и 40 делят дефицит так же, как 6 и 4), урезанный собственной
    /// потребностью каждой фабрики. Уровень фабрики (R&amp;D, SPEC §5.8) повышает эффективную
    /// скорость производства сверх базовой ставки рецепта.
    /// </summary>
    public static IReadOnlyList<ProductionResult> CalculateGroup(
        IReadOnlyList<Factory> factories, Warehouse warehouse, WorkerProductivityConfig productivity, RndConfig rnd)
    {
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(warehouse);
        ArgumentNullException.ThrowIfNull(productivity);
        ArgumentNullException.ThrowIfNull(rnd);

        var capacityOutputs = new Dictionary<Ulid, decimal>();
        var desiredBatches = new Dictionary<Ulid, decimal>();
        foreach (var factory in factories)
        {
            var effectiveCapacity = CalculateEffectiveCapacity(factory.Workers, productivity);
            var levelBonus = 1m + (factory.Level - 1) * rnd.ProductionRateBonusPerLevel;
            // На простое (SPEC §5.6, вынужденном или по капремонту, WearStep) выпуск домножен на
            // RepairOutputMultiplier — зафиксированный при начале именно этого простоя (0 — полная
            // остановка у тяжёлых ступеней и вынужденного простоя; частичный у лёгкого обслуживания).
            var conditionOrRepairMultiplier = factory.IsUnderRepair ? factory.RepairOutputMultiplier : factory.Condition;
            var capacityLimitedOutput = factory.SelectedRecipe.ProductionRate * levelBonus * conditionOrRepairMultiplier * effectiveCapacity;

            capacityOutputs[factory.Id] = capacityLimitedOutput;
            desiredBatches[factory.Id] = capacityLimitedOutput / factory.SelectedRecipe.OutputQuantity;
        }

        var quotas = AllocateSharedMaterials(factories, warehouse, desiredBatches);

        var results = new List<ProductionResult>();
        foreach (var factory in factories)
        {
            var recipe = factory.SelectedRecipe;
            var batches = desiredBatches[factory.Id];
            var availableByMaterial = new Dictionary<string, decimal>();
            foreach (var input in recipe.Inputs)
            {
                var available = quotas.TryGetValue((factory.Id, input.Material.Id), out var quota)
                    ? quota
                    : warehouse.QuantityOf(input.Material);
                availableByMaterial[input.Material.Id] = available;

                var batchesFromInput = available / input.Quantity;
                if (batchesFromInput < batches)
                {
                    batches = batchesFromInput;
                }
            }
            batches = Math.Max(batches, 0m);

            // Min с тем же available, что ограничил batches (квота при конкуренции, иначе остаток
            // склада), а не с заново запрошенным warehouse.QuantityOf: несколько фабрик группы
            // считаются по одному и тому же снимку склада до того, как хоть одна из них реально его
            // спишет (события применяются по одной, уже после CalculateGroup) — клэмп по общему
            // остатку склада вместо своей квоты позволил бы им в сумме запросить больше, чем реально
            // есть, и Warehouse.Remove бросил бы исключение на второй фабрике. Заодно decimal-деление
            // и обратное умножение здесь не точны — без клэмпа возможно исчезающее превышение и без
            // всякой конкуренции, тем более рискованно с ней.
            var consumedInputs = recipe.Inputs.ToDictionary(
                input => input.Material.Id,
                input => Math.Min(batches * input.Quantity, availableByMaterial[input.Material.Id]));

            results.Add(new ProductionResult
            {
                FactoryId = factory.Id,
                CapacityLimitedOutputQuantity = capacityOutputs[factory.Id],
                OutputQuantity = batches * recipe.OutputQuantity,
                ConsumedInputs = consumedInputs,
            });
        }

        return results;
    }

    /// <summary>
    /// Для каждого материала, нужного больше чем одной фабрике группы: если сырья хватает на
    /// суммарное желаемое всеми — квоты не нужны (ничего не возвращается для этого материала, каждая
    /// фабрика берёт что просит). Если не хватает — делит доступный остаток между претендентами
    /// пропорционально <see cref="Factory.AllocationShare"/>, урезая квоту собственной потребностью
    /// фабрики. Излишек квоты, оставшийся у фабрики с меньшей потребностью, другим не
    /// перераспределяется — в рамках одного тика это осознанное упрощение.
    /// </summary>
    private static Dictionary<(Ulid FactoryId, string MaterialId), decimal> AllocateSharedMaterials(
        IReadOnlyList<Factory> factories, Warehouse warehouse, IReadOnlyDictionary<Ulid, decimal> desiredBatches)
    {
        var quotas = new Dictionary<(Ulid, string), decimal>();

        var materials = factories
            .SelectMany(factory => factory.SelectedRecipe.Inputs.Select(input => input.Material))
            .Distinct();

        foreach (var material in materials)
        {
            var contenders = factories
                .Select(factory => (Factory: factory, Input: factory.SelectedRecipe.Inputs.FirstOrDefault(i => i.Material == material)))
                .Where(entry => entry.Input is not null)
                .ToList();
            if (contenders.Count <= 1)
            {
                continue; // не за что конкурировать
            }

            var available = warehouse.QuantityOf(material);
            var wants = contenders.ToDictionary(
                entry => entry.Factory.Id,
                entry => desiredBatches[entry.Factory.Id] * entry.Input!.Quantity);
            if (wants.Values.Sum() <= available)
            {
                continue; // хватает всем — конкуренции по факту нет
            }

            var totalShare = contenders.Sum(entry => entry.Factory.AllocationShare);
            foreach (var (factory, input) in contenders)
            {
                var quota = totalShare > 0
                    ? Math.Min(wants[factory.Id], available * factory.AllocationShare / totalShare)
                    : 0m;
                quotas[(factory.Id, input!.Material.Id)] = quota;
            }
        }

        return quotas;
    }

    /// <summary>Эффективная мощность из числа рабочих: линейно до базовой численности, дальше — с убывающей отдачей.</summary>
    private static decimal CalculateEffectiveCapacity(int workers, WorkerProductivityConfig productivity)
    {
        if (workers <= productivity.BaseWorkerCount)
        {
            return workers;
        }

        var excessWorkers = workers - productivity.BaseWorkerCount;
        return productivity.BaseWorkerCount + excessWorkers * productivity.DiminishingReturnsFactor;
    }
}
