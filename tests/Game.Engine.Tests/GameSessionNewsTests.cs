using Game.Config.Economy;
using Game.Config.News;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Сквозной путь новостной ленты через <see cref="GameSession"/> (Блок 6.3, SPEC §4, §5.4, §13).</summary>
public class GameSessionNewsTests
{
    private static GameSession StartSession(
        IReadOnlyList<NewsItemConfig> news, IReadOnlyList<EconomyTrendPhaseConfig>? trendScenario = null)
    {
        var config = TestGameConfig.BuildWithNews(news, trendScenario);
        var teamId = Ulid.NewUlid();

        return GameSession.StartWithEndTurn(
            config, endTurn: 999,
            new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id },
            });
    }

    private static void ToNextSettlement(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Settlement))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    [Fact]
    public void RunTick_Publishes_A_Headline_Matching_The_Current_Trend()
    {
        // TrendScenario пуст -> ход 1 трактуется как Stable (Блок 6.3).
        var news = new[] { new NewsItemConfig { Id = "stable-1", Trend = EconomyTrend.Stable, Headline = "Рынок замер" } };
        var session = StartSession(news);

        var appended = session.RunTick(new Random(1));

        var published = Assert.IsType<NewsPublished>(appended.Single(e => e.Change is NewsPublished).Change);
        Assert.Equal("stable-1", published.NewsItemId);
        Assert.Equal(EconomyTrend.Stable, published.Trend);
        Assert.Equal("Рынок замер", published.Headline);
        Assert.Equal(1, published.Turn);
        Assert.True(session.State.NewsFeed.IsPublished("stable-1"));
    }

    /// <summary>
    /// Блок 11.9: лента — прогноз. На ходу 1 действует стабильность, но через <c>NewsLookaheadTurns</c>
    /// ходов начнётся спад — и заголовок первого хода предупреждает именно о спаде, а не описывает
    /// сегодняшний штиль.
    /// </summary>
    [Fact]
    public void RunTick_Publishes_A_Headline_About_The_Trend_That_Is_Still_Ahead()
    {
        var scenario = new[]
        {
            new EconomyTrendPhaseConfig
            {
                Trend = EconomyTrend.Stable, StartTurn = 1, EndTurn = 3,
                PriceChangePerTurn = 0m, CapacityChangePerTurn = 0m,
            },
            new EconomyTrendPhaseConfig
            {
                Trend = EconomyTrend.Down, StartTurn = 4, EndTurn = 20,
                PriceChangePerTurn = 0m, CapacityChangePerTurn = 0m,
            },
        };
        var news = new[]
        {
            new NewsItemConfig { Id = "stable-1", Trend = EconomyTrend.Stable, Headline = "Рынок замер" },
            new NewsItemConfig { Id = "down-1", Trend = EconomyTrend.Down, Headline = "Заказчики сокращают закупки" },
        };
        var session = StartSession(news, scenario);

        var appended = session.RunTick(new Random(1));

        var published = Assert.IsType<NewsPublished>(appended.Single(e => e.Change is NewsPublished).Change);
        Assert.Equal(EconomyTrend.Down, published.Trend);
        Assert.Equal(1, published.Turn);
    }

    /// <summary>
    /// Блок 11.9: пока в пуле есть не звучавшие заголовки — повторов нет; когда пул исчерпан, лента
    /// не замолкает, а возвращается к самому давнему заголовку, не повторяя предыдущий подряд.
    /// </summary>
    [Fact]
    public void RunTick_Exhausts_Fresh_Headlines_First_And_Then_Cycles_Without_Falling_Silent()
    {
        var news = new[]
        {
            new NewsItemConfig { Id = "stable-1", Trend = EconomyTrend.Stable, Headline = "Первая новость" },
            new NewsItemConfig { Id = "stable-2", Trend = EconomyTrend.Stable, Headline = "Вторая новость" },
        };
        var session = StartSession(news);

        var published = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var appended = session.RunTick(new Random(1));
            var newsEvent = Assert.Single(appended.Where(e => e.Change is NewsPublished));
            published.Add(((NewsPublished)newsEvent.Change).NewsItemId);

            ToNextSettlement(session);
        }

        Assert.Equal(2, published.Take(2).Distinct().Count()); // сначала оба свежих
        Assert.All(
            published.Zip(published.Skip(1)),
            pair => Assert.NotEqual(pair.First, pair.Second)); // и дальше — без повторов подряд
    }

    [Fact]
    public void PublishManualNews_Publishes_A_Specific_Item_Regardless_Of_Trend_And_Shares_The_Pool()
    {
        // TrendScenario пуст -> текущий тренд Stable, но ведущий вручную публикует заголовок Down.
        var news = new[]
        {
            new NewsItemConfig { Id = "down-1", Trend = EconomyTrend.Down, Headline = "Обвал цен на нефть" },
            new NewsItemConfig { Id = "down-2", Trend = EconomyTrend.Down, Headline = "Спрос падает" },
        };
        var session = StartSession(news);

        var entry = session.PublishManualNews("down-1");

        var published = Assert.IsType<NewsPublished>(entry.Change);
        Assert.Equal(EconomyTrend.Down, published.Trend);
        Assert.True(session.State.NewsFeed.IsPublished("down-1"));

        // Пул общий: автоматический подбор считает опубликованный вручную заголовок уже звучавшим и
        // предпочтёт ему свежий.
        var selected = NewsCalculator.SelectNext(news, session.State.NewsFeed, EconomyTrend.Down, new Random(1));
        Assert.Equal("down-2", selected!.Id);

        // Повторная ручная публикация не запрещена — с блока 11.9 пул переиспользуется.
        Assert.IsType<NewsPublished>(session.PublishManualNews("down-1").Change);
    }

    [Fact]
    public void PublishManualNews_Throws_For_An_Unknown_Item_Id()
    {
        var session = StartSession(Array.Empty<NewsItemConfig>());

        Assert.Throws<ArgumentException>(() => session.PublishManualNews("does-not-exist"));
    }
}
