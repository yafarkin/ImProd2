using Game.Config.Economy;
using Game.Config.Loading;

namespace Game.Engine;

/// <summary>
/// Опорная цена продажи системе — сколько команда выручит за единицу материала при нейтральной
/// экономике и ненасыщенном рынке (блок 11.6, <c>docs/external-economy.md</c> §9).
///
/// <para><b>Единственный шов между моделью ценообразования и всей статической оснасткой.</b> До
/// 11.6 инструменты (<c>ProductionCostLevelCalculator</c>, <c>TeamSteadyStateCalculator</c>,
/// <c>GenerationParityCheck</c>, §0-§2 диагностики, <see cref="IdealHallCalculator"/>) держали у себя
/// по копии правила «себестоимость × 1.30» — тринадцать мест, каждое со своей формулировкой. Теперь
/// правило одно и здесь: ветвление по <see cref="PricingModel"/> живёт в единственной точке, а
/// инструменты спрашивают «почём это продастся», не зная, откуда взялась цена.</para>
///
/// <para>«Опорная» — потому что это цена <b>без</b> поправок на состояние рынка: индекс нейтрален,
/// давление предложения нулевое. Так и должно быть: инструменты отвечают на вопрос «сходится ли
/// цепочка в принципе», а не «что будет на 47-м ходу при таком-то заливе». Реальную цену сделки
/// считает <see cref="MarketSaleCalculator"/>, и она всегда не выше опорной.</para>
/// </summary>
public static class SystemSaleReferencePriceCalculator
{
    /// <summary>Опорная цена по каждому материалу конфига. Ключ — код материала.</summary>
    public static IReadOnlyDictionary<string, decimal> CalculateAll(
        ResolvedGameConfig config, IReadOnlyDictionary<string, decimal> unitCostByMaterialId)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(unitCostByMaterialId);

        if (config.Raw.Economy.PricingModel == PricingModel.External)
        {
            return config.Raw.Economy.BaseMarketPerMaterial.ToDictionary(m => m.MaterialId, m => m.BaseSellPrice);
        }

        return unitCostByMaterialId.ToDictionary(
            pair => pair.Key,
            pair => pair.Value * MarketSaleCalculator.SystemSaleMarginMultiplier);
    }

    /// <summary>Опорная цена одного материала; 0, если материал не торгуется системой (нет записи в конфиге рынка).</summary>
    public static decimal PriceOf(IReadOnlyDictionary<string, decimal> referencePrices, string materialId)
    {
        ArgumentNullException.ThrowIfNull(referencePrices);
        ArgumentNullException.ThrowIfNull(materialId);

        return referencePrices.TryGetValue(materialId, out var price) ? price : 0m;
    }

    /// <summary>
    /// Прибыль уровня за ход при самом консервативном допущении: весь выпуск уходит системе,
    /// кросс-торговли нет.
    ///
    /// <code>
    /// прибыль = выпуск × цена(выход) − Σ вход × цена(вход) − собственный передел
    /// </code>
    ///
    /// <para><b>Входы оцениваются по своей ЦЕНЕ ПРОДАЖИ, а не по себестоимости</b> — это и есть
    /// починка дефекта 3 из <c>docs/economy-accounting-audit.md</c>, только записанная в общем виде.
    /// Маржа на входах уже начислена тому уровню, который их произвёл; начислять её второй раз здесь
    /// значит завышать прибыль по разу на каждом переделе вертикальной цепочки (на боевом
    /// <c>metallurgy.json</c> это давало завышение в 2.23×).</para>
    ///
    /// <para><b>Формула одна на обе модели ценообразования и при <see cref="PricingModel.CostPlus"/>
    /// тождественно сводится к прежней</b> <c>0.30 × собственный передел</c> — подстановкой
    /// <c>цена = себестоимость × 1.30</c>. Это не совпадение и не приближение: равенство точное, и
    /// оно закреплено тестом. Благодаря ему переезд оснастки на новую базу выручки не сдвинул ни
    /// одного числа в уже откалиброванных цепочках.</para>
    /// </summary>
    public static decimal ProfitPerTurn(
        decimal outputQuantity,
        decimal outputReferencePrice,
        decimal inputsAtReferencePrice,
        decimal conversionCost)
    {
        return outputQuantity * outputReferencePrice - inputsAtReferencePrice - conversionCost;
    }
}
