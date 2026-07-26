using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Расчёт производства одной фабрики за один тик (SPEC §4 — «производство снизу вверх по уровням»
/// делает Блок 4.4, здесь — сам расчёт для одной фабрики; SPEC §5.6 — производительность от
/// рабочих). Чистая функция: не трогает журнал и не мутирует состояние — результат оборачивается
/// в <see cref="FactoryProduced"/> и применяется вызывающим кодом через событие.
/// </summary>
public static class ProductionCalculator
{
    /// <summary>
    /// Считает, сколько фабрика произведёт за тик: мощность от числа рабочих (линейно до базовой
    /// численности, дальше — с убывающей отдачей, SPEC §5.6) ограничивает выпуск сверху; фактически
    /// доступное на складе сырьё может ограничить его ещё сильнее. Каждый вход рецепта тратится
    /// пропорционально фактически произведённому количеству. Уровень фабрики (R&amp;D, SPEC §5.8)
    /// повышает эффективную скорость производства сверх базовой ставки рецепта.
    /// </summary>
    public static ProductionResult Calculate(Factory factory, Warehouse warehouse, WorkerProductivityConfig productivity, RndConfig rnd)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(warehouse);
        ArgumentNullException.ThrowIfNull(productivity);
        ArgumentNullException.ThrowIfNull(rnd);

        var recipe = factory.SelectedRecipe;
        var effectiveCapacity = CalculateEffectiveCapacity(factory.Workers, productivity);
        var levelBonus = 1m + (factory.Level - 1) * rnd.ProductionRateBonusPerLevel;
        var capacityLimitedOutput = recipe.ProductionRate * levelBonus * effectiveCapacity;

        var batches = capacityLimitedOutput / recipe.OutputQuantity;
        foreach (var input in recipe.Inputs)
        {
            var batchesFromInput = warehouse.QuantityOf(input.Material) / input.Quantity;
            if (batchesFromInput < batches)
            {
                batches = batchesFromInput;
            }
        }
        batches = Math.Max(batches, 0m);

        // Min с фактическим остатком: decimal-деление batchesFromInput и обратное умножение здесь —
        // не точная операция (частное округляется до ограниченной точности), так что для входа,
        // который и задал лимит по batches, умножение обратно на input.Quantity может на исчезающую
        // долю превысить то, что реально на складе, — и Warehouse.Remove бросит исключение на ровном
        // месте. Списываем не больше, чем есть.
        var consumedInputs = recipe.Inputs.ToDictionary(
            input => input.Material.Id,
            input => Math.Min(batches * input.Quantity, warehouse.QuantityOf(input.Material)));

        return new ProductionResult
        {
            FactoryId = factory.Id,
            CapacityLimitedOutputQuantity = capacityLimitedOutput,
            OutputQuantity = batches * recipe.OutputQuantity,
            ConsumedInputs = consumedInputs,
        };
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
