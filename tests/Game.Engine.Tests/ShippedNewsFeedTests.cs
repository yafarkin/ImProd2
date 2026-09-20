using Game.Config.Economy;
using Game.Config.Loading;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// Критерий готовности блока 11.9 (<c>docs/external-economy.md</c> §5, §9) на поставляемом контенте:
/// прогон на всю партию не даёт ни одного хода без новости и ни одного повтора подряд, а сама лента
/// предупреждает о тренде заранее, а не пересказывает уже случившееся.
/// </summary>
public class ShippedNewsFeedTests
{
    private const int LongestSession = 98;

    private static ResolvedGameConfig ShippedConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "training-1-sector.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    /// <summary>
    /// Прогон именно через <see cref="GameSession"/>, а не через один <see cref="NewsCalculator"/>:
    /// проверяется вся связка «сценарий → опережение → пул → журнал», включая то, что событие
    /// действительно попадает в журнал каждый ход.
    /// </summary>
    [Fact]
    public void The_Feed_Speaks_Every_Turn_Of_The_Longest_Session_Without_Repeating_Itself()
    {
        var config = ShippedConfig();
        var session = GameSession.StartWithEndTurn(
            config, LongestSession,
            new[]
            {
                new TeamSpec
                {
                    Id = Ulid.NewUlid(), Name = "Команда А1", SectorId = config.Sectors.First().Id,
                },
            });

        var random = new Random(20260908);
        var published = new List<NewsPublished>();
        while (!session.State.IsFinished)
        {
            var turn = session.State.CurrentTurn;
            var appended = session.RunTick(random);
            var newsEvent = appended.SingleOrDefault(e => e.Change is NewsPublished);

            Assert.True(newsEvent is not null, $"ход {turn} остался без новости");
            published.Add((NewsPublished)newsEvent!.Change);

            while (!session.State.IsFinished && !(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Settlement))
            {
                session.AdvancePhase(PhaseTransitionTrigger.Timer);
            }
        }

        Assert.Equal(LongestSession, published.Count);
        Assert.All(
            published.Zip(published.Skip(1)),
            pair => Assert.NotEqual(pair.First.NewsItemId, pair.Second.NewsItemId));
    }

    /// <summary>
    /// Заголовок каждого хода относится к тренду, который наступит через <c>NewsLookaheadTurns</c>
    /// ходов — то есть игрок узнаёт о развороте до, а не после того, как он ударит по выручке.
    /// </summary>
    [Fact]
    public void Every_Headline_Describes_The_Trend_Of_A_Turn_That_Has_Not_Happened_Yet()
    {
        var config = ShippedConfig().Raw;
        var feed = new NewsFeed();
        var random = new Random(20260908);

        for (var turn = 1; turn <= LongestSession; turn++)
        {
            var forecast = NewsCalculator.ForecastTrend(turn, config.NewsLookaheadTurns, config.Economy.TrendScenario);
            var item = NewsCalculator.SelectNext(config.News, feed, forecast, random);

            Assert.True(item is not null, $"на ход {turn} нет заголовка для тренда {forecast}");
            Assert.Equal(forecast, item!.Trend);
            feed.Record(item.Id, turn);
        }
    }

    /// <summary>
    /// Библиотека покрывает все три тренда с запасом: на 98 ходов при трёх трендах повтор технически
    /// допустим, но не должен быть массовым. Порог сознательно мягкий — он ловит не «мало
    /// заголовков», а «пул тренда из пары строк», как было до блока 11.9.
    /// </summary>
    [Fact]
    public void Each_Trend_Has_A_Deep_Enough_Pool_Of_Headlines()
    {
        var news = ShippedConfig().Raw.News;

        foreach (var trend in Enum.GetValues<EconomyTrend>())
        {
            Assert.True(
                news.Count(item => item.Trend == trend) >= 20,
                $"в пуле тренда {trend} всего {news.Count(item => item.Trend == trend)} заголовков");
        }

        Assert.Equal(news.Count, news.Select(item => item.Id).Distinct().Count());
        Assert.Equal(news.Count, news.Select(item => item.Headline).Distinct().Count());
    }
}
