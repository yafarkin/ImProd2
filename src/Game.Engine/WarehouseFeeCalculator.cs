using Game.Config.Economy;

namespace Game.Engine;

/// <summary>
/// Плата за превышение бесплатного лимита склада (SPEC §5.7): гибрид — бесплатный лимит вместимости
/// плюс плата за единицу сверх него, посчитанная по суммарному остатку по всем материалам команды.
/// </summary>
public static class WarehouseFeeCalculator
{
    public readonly record struct Result(decimal OverageQuantity, decimal Fee);

    public static Result Calculate(decimal totalStockQuantity, WarehouseConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var overage = Math.Max(0m, totalStockQuantity - config.FreeCapacity);
        return new Result(overage, overage * config.OverageFeePerUnit);
    }
}
