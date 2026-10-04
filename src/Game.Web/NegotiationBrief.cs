using Game.Domain;

namespace Game.Web;

/// <summary>
/// «С чем идти в зал» — главный экран переговорщика (docs/manager-ui/README.md §4, блок 6, макет
/// mockups/negotiator.html): три числа, которые нужны у стола, по каждому материалу на складе. Сколько
/// можно отдать, ниже какой цены в убыток, сколько даёт система.
/// <para>
/// «Свободно» — остаток минус то, что наши фабрики заберут за ход при полной загрузке, и минус
/// обещанное по действующим контрактам. Это новый расчёт на уровне подачи (§4 «Переговорщик»), движок
/// его не знает. Оценка намеренно осторожная: фабрики считаются загруженными полностью, поэтому
/// свободного может оказаться больше, но не меньше.
/// </para>
/// </summary>
public static class NegotiationBrief
{
    /// <summary>
    /// Один материал: <see cref="OnStock"/> — сколько лежит, <see cref="Free"/> — сколько можно обещать
    /// другим; <see cref="UnitCost"/> — себестоимость единицы (<c>null</c> — неизвестна),
    /// <see cref="MarketPrice"/> — котировка системы (<c>null</c> — котировки нет).
    /// </summary>
    public sealed record Offer(Material Material, decimal OnStock, decimal Free, decimal? UnitCost, decimal? MarketPrice);

    /// <summary>
    /// <paramref name="stock"/> — остатки склада; <paramref name="consumptionPerTurn"/> — сколько каждого
    /// материала наши фабрики заберут за ход при полной загрузке; <paramref name="promised"/> — сколько
    /// уже обещано по действующим контрактам, где мы продавец. Материалы с нулевым остатком не попадают.
    /// </summary>
    public static IReadOnlyList<Offer> Build(
        IReadOnlyList<(Material Material, decimal Quantity, decimal? UnitCost)> stock,
        IReadOnlyDictionary<string, decimal> consumptionPerTurn,
        IReadOnlyDictionary<string, decimal> promised,
        IReadOnlyDictionary<string, decimal> marketPrices)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(consumptionPerTurn);
        ArgumentNullException.ThrowIfNull(promised);
        ArgumentNullException.ThrowIfNull(marketPrices);

        return stock
            .Where(entry => entry.Quantity > 0)
            .OrderBy(entry => entry.Material.Name, StringComparer.Ordinal)
            .Select(entry => new Offer(
                entry.Material,
                entry.Quantity,
                Math.Max(0m, entry.Quantity - consumptionPerTurn.GetValueOrDefault(entry.Material.Id) - promised.GetValueOrDefault(entry.Material.Id)),
                entry.UnitCost,
                marketPrices.TryGetValue(entry.Material.Id, out var price) ? price : null))
            .ToList();
    }

    /// <summary>
    /// Сколько каждого материала уже обещано по контрактам, где мы продавец: регулярный — объём
    /// ближайшей поставки, разовый — весь объём, пока его ход поставки не прошёл.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> Promised(IEnumerable<Contract> contracts, Ulid ourTeamId, int currentTurn)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        return contracts
            .Where(contract => contract.SellerTeamId == ourTeamId && contract.Status == ContractStatus.Active)
            .Where(contract => contract.Terms.Type == ContractType.Recurring || contract.Terms.SpotDeliveryTurn >= currentTurn)
            .GroupBy(contract => contract.Terms.Material.Id)
            .ToDictionary(group => group.Key, group => group.Sum(contract => contract.Terms.Volume), StringComparer.Ordinal);
    }
}
