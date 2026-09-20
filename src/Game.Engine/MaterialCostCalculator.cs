using Game.Config.Loading;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Единая, статическая себестоимость каждого материала конфига (запрос пользователя, rebalance/2-sector-stepwise,
/// 2026-08-21: «НЕТ НИКАКОЙ РЫНОЧНОЙ ЦЕНЫ! Есть себестоимость материала, которую мы прекрасно можем
/// посчитать») — рекурсивно по рецепту (сырьё: содержание фабрики + электричество + зарплата при
/// <see cref="Config.Economy.WorkerProductivityConfig.BaseWorkerCount"/> рабочих; передел: себестоимость
/// прямых входов плюс то же самое), один раз на весь конфиг, одна и та же для всех команд и для системы —
/// заменяет собой рыночную котировку (<see cref="Market"/>) как источник цены сделки везде, где раньше
/// её брали: продажа системе (<see cref="MarketSaleCalculator"/>), аварийная закупка (<see
/// cref="EmergencyPurchaseStep"/>), заявки ботов в стакане (<c>Game.Bots.SimpleBot</c>). В отличие от
/// <c>Game.Balancing.ProductionCostLevelCalculator</c> (инструмент `--mode cost-levels`, зарплата
/// сознательно исключена — та величина для сравнения отраслей между собой, не для реальных денег) здесь
/// зарплата ВКЛЮЧЕНА — это настоящий экономический якорь, деньги по нему реально движутся.
/// </summary>
public static class MaterialCostCalculator
{
    /// <summary>Считает себестоимость каждого материала конфига один раз. Ключ — <see cref="Material.Id"/>.</summary>
    public static IReadOnlyDictionary<string, decimal> CalculateAll(ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var fixedCostByFactoryId = config.Raw.FactoryDefinitions.ToDictionary(d => d.Id, d => d.FixedCostPerTurn);
        var electricityRate = config.Raw.Economy.ElectricityConsumptionPerOutputUnit;
        var electricityPrice = config.Raw.Economy.ElectricityBasePrice;
        var productivity = config.Raw.WorkerProductivity;
        var rnd = config.Raw.Rnd;
        var workers = productivity.BaseWorkerCount;
        var salaryCost = workers * productivity.SalaryPerWorkerPerTurn;

        var producerByMaterialId = new Dictionary<string, (FactoryDefinition FactoryDef, Recipe Recipe)>();
        foreach (var factoryDef in config.FactoryDefinitions)
        {
            foreach (var recipe in factoryDef.Recipes)
            {
                producerByMaterialId.TryAdd(recipe.Output.Id, (factoryDef, recipe));
            }
        }

        var costByMaterialId = new Dictionary<string, decimal>();
        var inProgress = new HashSet<string>();

        decimal Resolve(string materialId)
        {
            if (costByMaterialId.TryGetValue(materialId, out var cached))
            {
                return cached;
            }
            if (!inProgress.Add(materialId))
            {
                throw new NotSupportedException($"Циклическая зависимость рецептов через материал '{materialId}'.");
            }

            if (!producerByMaterialId.TryGetValue(materialId, out var producer))
            {
                throw new NotSupportedException(
                    $"Материал '{materialId}' используется как вход рецепта, но ни одна фабрика в конфиге его не производит.");
            }

            var (factoryDef, recipe) = producer;
            var factory = new Factory(Ulid.NewUlid(), factoryDef.Sector, factoryDef, recipe);
            factory.Hire(workers);
            var breakdown = ProductionCalculator.CalculateCapacityBreakdown(factory, productivity, rnd);
            var outputQuantity = breakdown.TheoreticalMaxOutput;
            var batches = recipe.OutputQuantity > 0 ? outputQuantity / recipe.OutputQuantity : 0m;

            // Канонический порядок (по коду материала, не как перечислено в JSON, AGENTS §2, правило 6) —
            // суммирование decimal чувствительно к порядку слагаемых в последнем знаке, а «зеркальные»
            // сектора с одинаковыми числами, но разным порядком входов в рецепте, обязаны давать
            // побитово одинаковый результат (см. Game.Bots.Tests.SectorSymmetryRegressionTests).
            var inputCost = recipe.Inputs
                .OrderBy(input => input.Material.Id, StringComparer.Ordinal)
                .Sum(input => input.Quantity * batches * Resolve(input.Material.Id));
            var fixedCostPerTurn = fixedCostByFactoryId[factoryDef.Id];
            var electricityCost = outputQuantity * electricityRate * electricityPrice;
            var totalCost = inputCost + fixedCostPerTurn + electricityCost + salaryCost;
            var unitCost = outputQuantity > 0 ? totalCost / outputQuantity : 0m;

            inProgress.Remove(materialId);
            costByMaterialId[materialId] = unitCost;
            return unitCost;
        }

        foreach (var materialId in producerByMaterialId.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            Resolve(materialId);
        }

        return costByMaterialId;
    }
}
