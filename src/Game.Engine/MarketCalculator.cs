using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Функция состояния внешней экономики на заданный ход (Блок 6.1, SPEC §5.4-5.5): по каждому
/// материалу выдаёт (цену, ёмкость), плюс цену электричества. Чистая функция от (ход, конфиг) — не
/// зависит от фактических продаж, поэтому детерминирована и безопасна для повторного вызова на один
/// и тот же ход (AGENTS §2, правило 6).
///
/// <para><b>Две модели, см. <see cref="PricingModel"/>.</b></para>
///
/// <para><see cref="PricingModel.External"/> (блок 11.5, <c>docs/external-economy.md</c> §2.1):
/// и цена, и ёмкость — базовые значения из конфига, растянутые индексом деловой активности.
/// Публикуемая цена — это цена <b>ненасыщенного</b> рынка: просадку за перепроизводство накладывает
/// уже сама продажа (<see cref="MarketSaleCalculator"/>), потому что она зависит от того, сколько
/// зал успел продать, а котировка обязана оставаться чистой функцией от хода. Цена электричества
/// трендом не движется вовсе — индекс двигает только сторону спроса (решение пользователя,
/// 2026-09-07), а электричество это издержка.</para>
///
/// <para><see cref="PricingModel.CostPlus"/> — прежнее поведение: кусочно-постоянные абсолютные
/// приращения цены и ёмкости, накопленные с первого хода включительно; вне заданных сценарием
/// отрезков экономика не движется. Цена и ёмкость не уходят в минус. Сохранено ради
/// воспроизводимости уже снятых ботовых прогонов (<c>docs/TODO.md</c> №27).</para>
/// </summary>
public static class MarketCalculator
{
    public static MarketUpdateResult Calculate(int turn, EconomyConfig economy)
    {
        ArgumentNullException.ThrowIfNull(economy);
        if (turn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(turn), turn, "Turn must be positive.");
        }

        return economy.PricingModel == PricingModel.External
            ? CalculateExternal(turn, economy)
            : CalculateCostPlus(turn, economy);
    }

    private static MarketUpdateResult CalculateExternal(int turn, EconomyConfig economy)
    {
        var index = EconomyIndexCalculator.Calculate(turn, economy);

        var quotes = new Dictionary<string, MaterialQuote>();
        foreach (var baseline in economy.BaseMarketPerMaterial)
        {
            quotes[baseline.MaterialId] = new MaterialQuote(
                baseline.BasePrice * index,
                ExternalPriceCalculator.Capacity(baseline.BaseCapacity, index));
        }

        return new MarketUpdateResult
        {
            Quotes = quotes,
            ElectricityPrice = economy.ElectricityBasePrice,
        };
    }

    private static MarketUpdateResult CalculateCostPlus(int turn, EconomyConfig economy)
    {
        var (priceDelta, capacityDelta) = AccumulateTrend(turn, economy.TrendScenario);

        var quotes = new Dictionary<string, MaterialQuote>();
        foreach (var baseline in economy.BaseMarketPerMaterial)
        {
            quotes[baseline.MaterialId] = new MaterialQuote(
                Math.Max(0m, baseline.BasePrice + priceDelta),
                Math.Max(0m, baseline.BaseCapacity + capacityDelta));
        }

        return new MarketUpdateResult
        {
            Quotes = quotes,
            ElectricityPrice = Math.Max(0m, economy.ElectricityBasePrice + priceDelta),
        };
    }

    private static (decimal PriceDelta, decimal CapacityDelta) AccumulateTrend(
        int turn, IReadOnlyList<EconomyTrendPhaseConfig> trendScenario)
    {
        var priceDelta = 0m;
        var capacityDelta = 0m;

        for (var t = 1; t <= turn; t++)
        {
            var phase = trendScenario.FirstOrDefault(p => t >= p.StartTurn && t <= p.EndTurn);
            if (phase is null)
            {
                continue;
            }

            priceDelta += phase.PriceChangePerTurn;
            capacityDelta += phase.CapacityChangePerTurn;
        }

        return (priceDelta, capacityDelta);
    }
}
