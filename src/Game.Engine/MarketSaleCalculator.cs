using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Расчёт продажи материала системе (Блок 6.1, SPEC §5.4): в пределах оставшейся на этот ход
/// ёмкости — по себестоимости (<see cref="MaterialCostCalculator"/>, не рыночной котировке) ×
/// <see cref="SystemSaleMarginMultiplier"/>; сверх ёмкости — та же цена с дополнительным понижающим
/// коэффициентом (перепроизводство обваливает цену продажи). Наценка — фиксированная, ОДНА на все
/// материалы независимо от уровня передела (запрос пользователя, rebalance/2-sector-stepwise,
/// 2026-08-22: «цена продажи системе = себестоимость материала + 5%, вне зависимости от уровня»,
/// расширено тем же днём до 10%/40%-окна для продажи/аварийной закупки — «маркетмейкер» шире, чем
/// изначально) — до этого была настраиваемая таблица по уровню (<c>Economy.MarginMultiplierByProcessingLevel</c>),
/// убрана целиком: асимметрично подобранные множители соседних уровней дважды (step8, step12)
/// оказывались источником бага «переработка почти не приносит прибыли», хотя себестоимость по цепочке
/// росла честно. Ёмкость (<see cref="Market.RemainingCapacityOf"/>) осталась привязана к рынку — это
/// отдельный, не связанный с ценой механизм (сколько система готова выкупить за ход, не почём).
/// Чистая функция — не мутирует ни склад, ни рынок.
/// </summary>
public static class MarketSaleCalculator
{
    /// <summary>
    /// Наценка системной продажи над себестоимостью — везде и всегда 1.30× (себестоимость + 30%,
    /// поднято с 20% тем же днём, step17), не зависит от уровня передела материала (см. doc-comment
    /// класса). Положительная, но меньше аварийного плана (тот — <see
    /// cref="EconomyConfig.EmergencyPurchaseBaseMultiplier"/>, обычно намного больше).
    /// </summary>
    public const decimal SystemSaleMarginMultiplier = 1.30m;

    public static MarketSaleResult Calculate(
        Market market, IReadOnlyDictionary<string, decimal> materialCosts, EconomyConfig economy, Material material, decimal volume)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(materialCosts);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(material);
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Sale volume must be positive.");
        }

        var unitCost = materialCosts.TryGetValue(material.Id, out var cost) ? cost : 0m;
        var remainingCapacity = market.RemainingCapacityOf(material.Id);

        var unitPrice = unitCost * SystemSaleMarginMultiplier;
        var withinCapacityVolume = Math.Min(volume, remainingCapacity);
        var overflowVolume = volume - withinCapacityVolume;
        var overflowUnitPrice = unitPrice * economy.MarketCapacityOverflowDiscount;

        return new MarketSaleResult
        {
            WithinCapacityVolume = withinCapacityVolume,
            OverflowVolume = overflowVolume,
            UnitPrice = unitPrice,
            TotalRevenue = withinCapacityVolume * unitPrice + overflowVolume * overflowUnitPrice,
        };
    }
}
