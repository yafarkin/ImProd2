using Game.Config.Economy;
using Game.Config.News;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Подбор следующего заголовка новостной ленты (Блок 6.3, SPEC §5.4, §13): «тренд → пул
/// заголовков», без повторов в пределах сессии.
/// </summary>
public static class NewsCalculator
{
    /// <summary>
    /// Тренд, действующий на заданный ход — тот же сценарный отрезок, что двигает цену и ёмкость в
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
    /// Случайно выбирает ещё не опубликованный в этой сессии заголовок текущего тренда; null, если
    /// пул заголовков для этого тренда в библиотеке исчерпан — в этот ход новости просто не будет,
    /// повтор силой не допускается (AGENTS §2, правило 6: случайность — только через переданный,
    /// при необходимости засеянный, <see cref="Random"/>).
    /// </summary>
    public static NewsItemConfig? SelectNext(
        IReadOnlyList<NewsItemConfig> library, NewsFeed feed, EconomyTrend currentTrend, Random random)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(random);

        var candidates = library.Where(item => item.Trend == currentTrend && !feed.IsPublished(item.Id)).ToList();
        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }
}
