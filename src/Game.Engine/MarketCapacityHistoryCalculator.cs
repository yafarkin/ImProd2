using Game.Config.Loading;

namespace Game.Engine;

/// <summary>
/// Живой остаток дневной ёмкости сырья в рамках текущего хода, для графика на большом экране
/// (запрос пользователя: видеть, как чья-то крупная продажа «съедает» ёмкость по ходу дела, а не
/// только по итогам хода). Движок не хранит эту историю сам по себе — как и остальная историческая
/// аналитика (<see cref="FactoryHistoryCalculator"/>, <see cref="FinanceHistoryCalculator"/>), она
/// восстанавливается проигрыванием уже записанного журнала на копии состояния.
///
/// Важно: базовая цена материала (<see cref="Game.Domain.MaterialQuote.Price"/>) фиксирована на
/// весь ход и не зависит от объёма продаж (см. doc-comment <see cref="MarketCalculator"/>) — падать
/// в реальном времени в рамках хода может только остаток ёмкости
/// (<see cref="Game.Domain.Market.RemainingCapacityOf"/>): каждая продажа списывает его немедленно
/// (<see cref="MaterialSoldToSystem.Apply"/>), и когда он доходит до нуля, дальнейшие продажи идут
/// со скидкой за превышение (<see cref="Game.Config.Economy.EconomyConfig.MarketCapacityOverflowDiscount"/>),
/// а не по обвалившейся базовой цене. Поэтому график строит именно остаток ёмкости, а не цену.
/// </summary>
public static class MarketCapacityHistoryCalculator
{
    /// <summary>
    /// По коду сырьевого материала — точки «секунды с начала текущего хода → остаток дневной
    /// ёмкости, % от котировки хода», в порядке возрастания времени. Ось X — реальное (настенное)
    /// время между записями журнала (<see cref="EventLogEntry{TState}.Timestamp"/>), а не игровое:
    /// именно так получаем честный «график в реальном времени», не заводя отдельной инфраструктуры
    /// подвыборки. Данные — только текущего (последнего в журнале) хода: смена хода
    /// (<see cref="MarketUpdated"/> или, для первого хода, <see cref="SessionStarted"/>) сбрасывает
    /// накопленные точки — прошлые ходы для этой картинки не нужны. Материал без продаж в текущем
    /// ходу в словарь не попадает — вызывающая сторона сама решает, чем заполнить пробел (обычно
    /// одной точкой «100% на начало хода»).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<(int ElapsedSeconds, decimal RemainingCapacityPercentage)>>
        SummarizeCurrentTurn(IReadOnlyList<EventLogEntry<GameSessionState>> entries, ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(config);

        var scratch = new GameSessionState(config);
        var pointsByMaterialId = new Dictionary<string, List<(int ElapsedSeconds, decimal RemainingCapacityPercentage)>>();
        var turnStartTimestamp = default(DateTimeOffset);

        foreach (var entry in entries)
        {
            entry.Change.Apply(scratch);

            if (entry.Change is MarketUpdated or SessionStarted)
            {
                pointsByMaterialId.Clear();
                turnStartTimestamp = entry.Timestamp;

                foreach (var material in config.Materials.Values)
                {
                    if (material.IsRawMaterial && scratch.Market.HasQuote(material.Id))
                    {
                        pointsByMaterialId[material.Id] = [(0, 100m)];
                    }
                }

                continue;
            }

            if (entry.Change is MaterialSoldToSystem sold && config.Materials[sold.MaterialId].IsRawMaterial
                && pointsByMaterialId.TryGetValue(sold.MaterialId, out var points))
            {
                var quote = scratch.Market.QuoteOf(sold.MaterialId);
                var percentage = quote.Capacity > 0
                    ? scratch.Market.RemainingCapacityOf(sold.MaterialId) / quote.Capacity * 100m
                    : 0m;
                var elapsedSeconds = (int)(entry.Timestamp - turnStartTimestamp).TotalSeconds;

                points.Add((elapsedSeconds, percentage));
            }
        }

        return pointsByMaterialId.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<(int ElapsedSeconds, decimal RemainingCapacityPercentage)>)pair.Value);
    }
}
