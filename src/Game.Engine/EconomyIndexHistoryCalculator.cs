using Game.Config.Loading;

namespace Game.Engine;

/// <summary>
/// История индекса деловой активности по ходам — данные для графика «состояние внешней экономики»
/// на большом экране и у команды (блок 11.3, <c>docs/external-economy.md</c> §6). Как и остальная
/// историческая аналитика (<see cref="FactoryHistoryCalculator"/>,
/// <see cref="FinanceHistoryCalculator"/>, <see cref="MarketCapacityHistoryCalculator"/>), движок
/// эту историю отдельно не хранит — она восстанавливается проигрыванием уже записанного журнала на
/// копии состояния.
///
/// <para><b>Показывается только прошлое, включая текущий ход — никогда будущее.</b> Индекс — чистая
/// функция от (ход, конфиг), то есть весь сценарий тренда до конца партии технически можно было бы
/// посчитать вперёд и нарисовать. Делать этого нельзя: прогноз в игре — работа новостной ленты
/// (качественный заголовок, к которому надо готовиться), а не графика с точными числами. График
/// показывает, что уже случилось; куда пойдёт дальше — игрок выводит из новостей и своей готовности
/// рискнуть.</para>
///
/// <para>Именно поэтому источник значений — журнал, а не <see cref="EconomyIndexCalculator"/>
/// напрямую: проигрывание отдаёт то, что реально было опубликовано командам, и физически не может
/// заглянуть за последнюю запись журнала.</para>
/// </summary>
public static class EconomyIndexHistoryCalculator
{
    /// <summary>Одна точка графика: ход и значение индекса, опубликованное на этом ходу.</summary>
    public readonly record struct IndexPoint(int Turn, decimal Index);

    /// <summary>
    /// Точки «ход → индекс» в порядке возрастания хода. Индекс публикуется дважды по-разному:
    /// на первый ход — событием <see cref="SessionStarted"/> (рынок первого хода публикуется прямо
    /// им, отдельного <see cref="MarketUpdated"/> на него нет), дальше — каждым
    /// <see cref="MarketUpdated"/>. Повторная публикация на тот же ход заменяет прежнее значение, а
    /// не добавляет вторую точку: за ход у экономики одно состояние.
    /// </summary>
    public static IReadOnlyList<IndexPoint> Summarize(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries, ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(config);

        var scratch = new GameSessionState(config);
        var indexByTurn = new Dictionary<int, decimal>();

        foreach (var entry in entries)
        {
            entry.Change.Apply(scratch);

            if (entry.Change is SessionStarted or MarketUpdated)
            {
                indexByTurn[scratch.CurrentTurn] = scratch.Market.EconomyIndex;
            }
        }

        return indexByTurn
            .OrderBy(pair => pair.Key)
            .Select(pair => new IndexPoint(pair.Key, pair.Value))
            .ToList();
    }
}
