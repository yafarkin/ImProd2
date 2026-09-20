using Game.Config.Economy;
using Game.Config.News;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Подбор следующего заголовка новостной ленты (Блок 6.3, SPEC §5.4, §13; переработан блоком 11.9,
/// <c>docs/external-economy.md</c> §5): «тренд → пул заголовков», сначала не звучавшие, при
/// исчерпании пула — самый давний из уже звучавших.
///
/// <para><b>Лента — прогноз, а не хроника.</b> Заголовок хода <c>t</c> берётся из пула тренда,
/// который будет действовать на ходу <c>t + NewsLookaheadTurns</c> (<see cref="ForecastTrend"/>).
/// Иначе новость сообщала бы об уже применившемся к этому же ходу тренде и готовиться к ней было бы
/// поздно — а именно ради «понимать, к чему готовиться» лента и заведена.</para>
/// </summary>
public static class NewsCalculator
{
    /// <summary>
    /// Тренд, действующий на заданный ход — тот же сценарный отрезок, что двигает индекс и ёмкость в
    /// <see cref="MarketCalculator"/>. Ход вне всех отрезков сценария считается стабильным: рынок в
    /// это время и так не движется, значит по смыслу это и есть «стабильность».
    /// </summary>
    public static EconomyTrend CurrentTrend(int turn, IReadOnlyList<EconomyTrendPhaseConfig> trendScenario)
    {
        ArgumentNullException.ThrowIfNull(trendScenario);

        var phase = trendScenario.FirstOrDefault(p => turn >= p.StartTurn && turn <= p.EndTurn);
        return phase?.Trend ?? EconomyTrend.Stable;
    }

    /// <summary>
    /// Тренд, о котором новость хода <paramref name="turn"/> предупреждает заранее: тот, что будет
    /// действовать через <paramref name="lookaheadTurns"/> ходов. При нулевом опережении вырождается
    /// в <see cref="CurrentTrend"/> — то есть в прежнее поведение «лента как хроника».
    /// </summary>
    public static EconomyTrend ForecastTrend(
        int turn, int lookaheadTurns, IReadOnlyList<EconomyTrendPhaseConfig> trendScenario)
    {
        if (lookaheadTurns < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookaheadTurns), lookaheadTurns, "News lookahead must not be negative.");
        }

        return CurrentTrend(turn + lookaheadTurns, trendScenario);
    }

    /// <summary>
    /// Выбирает заголовок данного тренда: случайно среди ещё не звучавших в этой сессии, а если все
    /// они уже прозвучали — самый давно не звучавший (LRU по <see cref="NewsFeed.LastPublishedTurn"/>,
    /// при равенстве ходов — случайно). Возвращает null только тогда, когда заголовков этого тренда
    /// нет в библиотеке вовсе: это дыра в контенте, а не исчерпание пула.
    ///
    /// <para>Заголовок, прозвучавший прошлым ходом, исключается из кандидатов — лента не повторяется
    /// подряд, даже если пул тренда состоит из одной строки. Единственное исключение — когда такой
    /// заголовок в пуле единственный: тогда выбора нет и молчать хуже, чем повториться.</para>
    ///
    /// <para>Случайность — только через переданный, при необходимости засеянный,
    /// <see cref="Random"/> (AGENTS §2, правило 6).</para>
    /// </summary>
    public static NewsItemConfig? SelectNext(
        IReadOnlyList<NewsItemConfig> library, NewsFeed feed, EconomyTrend currentTrend, Random random)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(random);

        var pool = library.Where(item => item.Trend == currentTrend).ToList();
        if (pool.Count == 0)
        {
            return null;
        }

        var candidates = pool.Count > 1
            ? pool.Where(item => item.Id != feed.LastPublishedItemId).ToList()
            : pool;

        var fresh = candidates.Where(item => !feed.IsPublished(item.Id)).ToList();
        if (fresh.Count > 0)
        {
            return fresh[random.Next(fresh.Count)];
        }

        var oldestTurn = candidates.Min(item => feed.LastPublishedTurn(item.Id) ?? int.MinValue);
        var oldest = candidates.Where(item => (feed.LastPublishedTurn(item.Id) ?? int.MinValue) == oldestTurn).ToList();
        return oldest[random.Next(oldest.Count)];
    }
}
