using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Функция состояния внешней экономики на заданный ход (Блок 6.1, SPEC §5.4-5.5): по каждому
/// материалу выдаёт (цену, ёмкость), плюс цену электричества. Тренд сессии — кусочно-постоянное
/// изменение за ход (<see cref="EconomyTrendPhaseConfig"/>), накопленное с первого хода
/// включительно; вне заданных сценарием отрезков экономика не движется (изменение считается
/// нулевым). Цена и ёмкость никогда не уходят в минус. Чистая функция от (ход, конфиг) — не
/// зависит от фактических продаж, поэтому детерминирована и безопасна для повторного вызова на
/// один и тот же ход (AGENTS §2, правило 6).
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
