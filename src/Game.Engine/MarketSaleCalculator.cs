using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Расчёт продажи материала системе (Блок 6.1, SPEC §5.4): в пределах оставшейся на этот ход
/// ёмкости — по котировке × множитель маржи уровня передела; сверх ёмкости — та же цена с
/// дополнительным понижающим коэффициентом (перепроизводство обваливает цену продажи). Продукция
/// любого уровня передела, включая сырьё, — множитель по умолчанию 1, если для уровня в конфиге
/// нет отдельной записи (SPEC §5.4: наценка — стимул для переделов выше базового, не обязанность
/// конфигурировать каждый уровень явно). Чистая функция — не мутирует ни склад, ни рынок.
/// </summary>
public static class MarketSaleCalculator
{
    public static MarketSaleResult Calculate(Market market, EconomyConfig economy, Material material, decimal volume)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(material);
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Sale volume must be positive.");
        }

        var quote = market.QuoteOf(material.Id);
        var remainingCapacity = market.RemainingCapacityOf(material.Id);
        var marginMultiplier = economy.MarginMultiplierByProcessingLevel
            .FirstOrDefault(m => m.Level == material.Level)?.MarginMultiplier ?? 1m;

        var unitPrice = quote.Price * marginMultiplier;
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
