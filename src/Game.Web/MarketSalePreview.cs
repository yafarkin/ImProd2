using Game.Config.Economy;
using Game.Domain;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// Предпросмотр продажи системе для страницы команды (блок 11.10, <c>docs/external-economy.md</c> §6):
/// что игрок получит за заявленный объём, <b>до</b> подачи заявки. Считается той же чистой функцией
/// <see cref="MarketSaleCalculator.Calculate"/>, что и реальная продажа на расчёте, поэтому
/// предпросмотр не может разойтись с фактом.
///
/// <para><b>Главное, что здесь показывается сверх цены, — своя собственная просадка.</b> Под
/// экзогенной моделью цена непрерывно падает с объёмом (<see cref="ExternalPriceCalculator"/>), и
/// без этой подписи игрок узнавал бы о том, что залил рынок, только по факту зачисления: заявка
/// вдвое больше приносит заметно меньше, чем вдвое больше денег. Раньше на этом месте была «полка»
/// с отдельным тарифом сверх ёмкости — её и заменяет пара «цена без заявки → средняя цена сделки».</para>
/// </summary>
public static class MarketSalePreview
{
    /// <summary>
    /// <see cref="UnitPrice"/> — средняя цена по всему объёму заявки, <see cref="MarginalUnitPrice"/>
    /// — цена рынка до этой заявки (с учётом уже состоявшихся продаж зала, в том числе чужих).
    /// <see cref="PriceImpactRate"/> — насколько заявка просаживает цену сама себе, доля от
    /// <see cref="MarginalUnitPrice"/>. <see cref="OverflowVolume"/> — часть объёма, уходящая за
    /// ёмкость рынка (под экзогенной моделью это не отдельный тариф, а сигнал «рынок насыщен», см.
    /// <see cref="MarketSaleCalculator"/>). <see cref="Profit"/> считается по реальной себестоимости
    /// остатка на складе (<see cref="Warehouse.AverageCostOf"/>), а не по рыночной цене — та
    /// отвечает на другой вопрос.
    /// </summary>
    public sealed record Result(
        bool HasQuote,
        decimal UnitPrice,
        decimal MarginalUnitPrice,
        decimal PriceImpactRate,
        decimal OverflowVolume,
        decimal Revenue,
        decimal Profit);

    /// <summary>Просадка, ниже которой её не стоит и показывать — округление и шум, не сигнал.</summary>
    public const decimal NoticeablePriceImpactRate = 0.005m;

    /// <summary>
    /// <paramref name="entries"/> и <paramref name="currentTurn"/> нужны для давления предложения:
    /// предпросмотр обязан показывать ту же цену, что реально получится, включая просадку от уже
    /// состоявшихся продаж зала — иначе игрок узнаёт о насыщении рынка только постфактум, и
    /// конкуренция за сбыт становится необъяснимой.
    /// </summary>
    public static Result Calculate(
        Market market, IReadOnlyDictionary<string, decimal> materialCosts, EconomyConfig economy,
        Material material, decimal volume, decimal realUnitCost,
        IReadOnlyList<EventLogEntry<GameSessionState>> entries, int currentTurn)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(materialCosts);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(entries);

        if (volume <= 0m || !market.HasQuote(material.Id))
        {
            return new Result(HasQuote: false, 0m, 0m, 0m, 0m, 0m, 0m);
        }

        var supplyPressure = economy.PricingModel == PricingModel.External
            ? MarketSupplyPressureCalculator.CalculateRecentVolume(entries, material.Id, currentTurn, economy)
            : 0m;

        var result = MarketSaleCalculator.Calculate(market, materialCosts, economy, material, volume, supplyPressure);
        var marginalPrice = MarketSaleCalculator.MarginalUnitPrice(market, materialCosts, economy, material, supplyPressure);
        var impact = marginalPrice > 0m ? 1m - result.UnitPrice / marginalPrice : 0m;

        return new Result(
            HasQuote: true,
            result.UnitPrice,
            marginalPrice,
            Math.Max(0m, impact),
            result.OverflowVolume,
            result.TotalRevenue,
            result.TotalRevenue - realUnitCost * volume);
    }
}
