using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Расчёт продажи материала системе (Блок 6.1, SPEC §5.4). Ветвится по <see cref="PricingModel"/> —
/// одна из двух точек движка, где модель ценообразования вообще различима (вторая — <see
/// cref="EmergencyPurchaseStep"/>), <c>docs/external-economy.md</c> §8. Чистая функция: не мутирует
/// ни склад, ни рынок.
///
/// <para><b><see cref="PricingModel.External"/></b> (блок 11.5) — цена задаётся внешней экономикой и
/// от себестоимости не зависит: <c>BaseSellPrice × Индекс × Эластичность(давление)</c>. Объём
/// продажи оценивается интегралом по кривой эластичности (<see
/// cref="ExternalPriceCalculator.AverageElasticityMultiplier"/>), поэтому выручка не зависит от
/// того, одним заказом продано или десятью. <c>WithinCapacityVolume</c>/<c>OverflowVolume</c> здесь
/// не два ценовых тарифа (тарифа больше нет, цена непрерывна), а <b>сигнал интерфейсу</b>: сколько
/// из проданного ушло в ещё не насыщенный рынок, а сколько — за его ёмкость.</para>
///
/// <para><b><see cref="PricingModel.CostPlus"/></b> — прежнее правило: себестоимость (<see
/// cref="MaterialCostCalculator"/>) × фиксированная наценка <see cref="SystemSaleMarginMultiplier"/>,
/// одна на все уровни передела; сверх ёмкости — та же цена с понижающим коэффициентом
/// (ступенька). Сохранено как калибровочно-регрессионный режим, не как игровая опция.</para>
/// </summary>
public static class MarketSaleCalculator
{
    /// <summary>
    /// Наценка системной продажи над себестоимостью в режиме <see cref="PricingModel.CostPlus"/> —
    /// 1.30× (себестоимость + 30%), не зависит от уровня передела. Это и есть то самое упрощение,
    /// ради отладки цепочек введённое 2026-08-21 и снимаемое Фазой 11: под ним экономика замкнута
    /// сама на себя (цена из себестоимости, себестоимость из цен входов), из-за чего не
    /// вознаграждается ни глубина передела, ни эффективность
    /// (<c>docs/economy-accounting-audit.md</c>).
    /// </summary>
    public const decimal SystemSaleMarginMultiplier = 1.30m;

    /// <param name="supplyPressure">
    /// Давление предложения по этому материалу ДО текущей продажи
    /// (<see cref="MarketSupplyPressureCalculator"/>). В режиме <see cref="PricingModel.CostPlus"/>
    /// не используется вовсе.
    /// </param>
    public static MarketSaleResult Calculate(
        Market market, IReadOnlyDictionary<string, decimal> materialCosts, EconomyConfig economy,
        Material material, decimal volume, decimal supplyPressure = 0m)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(materialCosts);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(material);
        if (volume <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Sale volume must be positive.");
        }

        return economy.PricingModel == PricingModel.External
            ? CalculateExternal(market, economy, material, volume, supplyPressure)
            : CalculateCostPlus(market, materialCosts, economy, material, volume);
    }

    /// <summary>
    /// Наибольшая доля <paramref name="desiredVolume"/>, при которой средняя цена сделки ещё не
    /// падает ниже <paramref name="minAcceptablePriceRate"/> от цены ненасыщенного рынка. Остальное
    /// продавцу выгоднее придержать до ходов, где давление предложения успеет затухнуть (блок 11.7).
    ///
    /// <para>Живёт здесь, а не у продавца, чтобы <b>бот и идеальный зал дросселировали одинаково</b>.
    /// Если бы придерживал только бот, зал перестал бы быть верхней границей X(t): реальная команда
    /// обыгрывала бы «идеальную» просто за счёт того, что не топит собственную цену.</para>
    ///
    /// <para>Считается бисекцией по этому же классу, поэтому не держит копии формулы цены и
    /// одинаково работает в обеих моделях. Число итераций фиксировано — результат детерминирован
    /// (AGENTS §2, правило 6). Если цена от объёма не зависит вовсе (cost-plus с выключенным штрафом
    /// за превышение ёмкости), проверка проходит на полном объёме и бисекция не запускается.</para>
    /// </summary>
    public static decimal LargestVolumeAbovePriceFloor(
        Market market, IReadOnlyDictionary<string, decimal> materialCosts, EconomyConfig economy,
        Material material, decimal desiredVolume, decimal supplyPressure, decimal minAcceptablePriceRate)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (desiredVolume <= 0m)
        {
            return 0m;
        }

        decimal AverageUnitPrice(decimal volume) =>
            Calculate(market, materialCosts, economy, material, volume, supplyPressure).UnitPrice;

        // Эталон — цена бесконечно малой продажи: неиспорченная цена этого хода с учётом уже
        // накопленного чужого залива, но без вклада самой этой сделки.
        var pristinePrice = AverageUnitPrice(Math.Min(desiredVolume, 0.0001m));
        if (pristinePrice <= 0m)
        {
            return desiredVolume;
        }

        var priceFloor = pristinePrice * minAcceptablePriceRate;
        if (AverageUnitPrice(desiredVolume) >= priceFloor)
        {
            return desiredVolume;
        }

        var low = 0m;
        var high = desiredVolume;
        for (var iteration = 0; iteration < 24; iteration++)
        {
            var middle = (low + high) / 2m;
            if (middle <= 0m)
            {
                break;
            }

            if (AverageUnitPrice(middle) >= priceFloor)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static MarketSaleResult CalculateExternal(
        Market market, EconomyConfig economy, Material material, decimal volume, decimal supplyPressure)
    {
        var quote = market.QuoteOf(material.Id);
        if (quote.Capacity <= 0m)
        {
            throw new InvalidOperationException(
                $"Material '{material.Id}' has no market capacity: the elasticity curve is undefined without it. " +
                "Give it a positive BaseCapacity in the production model.");
        }

        // Котировка уже несёт BaseSellPrice × Индекс (см. MarketCalculator), поэтому здесь остаётся
        // домножить её на среднюю эластичность — индекс второй раз не применяется.
        var averageUnitPrice = quote.Price
                               * ExternalPriceCalculator.AverageElasticityMultiplier(
                                   quote.Capacity, supplyPressure, volume, economy.MarketPriceFloorRate);

        // Не ценовые тарифы, а сигнал интерфейсу «сколько ушло в ненасыщенный рынок» — см.
        // doc-comment класса. Отсчитывается от давления, а не от Market.SoldThisTurn: под внешней
        // моделью насыщение живёт дольше одного хода.
        var headroom = Math.Max(0m, quote.Capacity - supplyPressure);
        var withinCapacityVolume = Math.Min(volume, headroom);

        return new MarketSaleResult
        {
            WithinCapacityVolume = withinCapacityVolume,
            OverflowVolume = volume - withinCapacityVolume,
            UnitPrice = averageUnitPrice,
            TotalRevenue = volume * averageUnitPrice,
        };
    }

    private static MarketSaleResult CalculateCostPlus(
        Market market, IReadOnlyDictionary<string, decimal> materialCosts, EconomyConfig economy,
        Material material, decimal volume)
    {
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
