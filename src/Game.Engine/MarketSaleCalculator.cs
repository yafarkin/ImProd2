using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Расчёт продажи материала системе (Блок 6.1, SPEC §5.4): в пределах оставшейся на этот ход
/// ёмкости — по себестоимости (<see cref="MaterialCostCalculator"/>, не рыночной котировке — запрос
/// пользователя, rebalance/2-sector-stepwise, 2026-08-21) × множитель маржи уровня передела; сверх
/// ёмкости — та же цена с дополнительным понижающим коэффициентом (перепроизводство обваливает цену
/// продажи). Множитель по умолчанию — <see cref="DefaultMarginMultiplier"/> (небольшая положительная
/// наценка), если для уровня в конфиге нет отдельной записи: до перехода на себестоимость (см. выше)
/// множитель применялся к произвольной рыночной котировке, «без наценки» (1×) там означало «продать
/// по официальной цене, без бонуса» — само по себе прибыльно, раз котировка уже выше себестоимости.
/// Теперь база — сама себестоимость, множитель 1× давал бы точно ноль прибыли, а не «отсутствие
/// бонуса» — не совпадает с запросом пользователя («небольшой %» касается любого уровня, включая
/// сырьё). Ёмкость (<see cref="Market.RemainingCapacityOf"/>) осталась привязана к рынку — это
/// отдельный, не связанный с ценой механизм (сколько система готова выкупить за ход, не почём).
/// Чистая функция — не мутирует ни склад, ни рынок.
/// </summary>
public static class MarketSaleCalculator
{
    /// <summary>Наценка системной продажи для уровня передела, для которого в конфиге нет отдельной записи в <see cref="EconomyConfig.MarginMultiplierByProcessingLevel"/> — небольшая, положительная (см. doc-comment класса).</summary>
    public const decimal DefaultMarginMultiplier = 1.05m;

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
        var marginMultiplier = economy.MarginMultiplierByProcessingLevel
            .FirstOrDefault(m => m.Level == material.Level)?.MarginMultiplier ?? DefaultMarginMultiplier;

        var unitPrice = unitCost * marginMultiplier;
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
