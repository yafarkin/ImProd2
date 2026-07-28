using Game.Config.Economy;
using Game.Config.News;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Сквозной путь новостной ленты через <see cref="GameSession"/> (Блок 6.3, SPEC §4, §5.4, §13).</summary>
public class GameSessionNewsTests
{
    private static GameSession StartSession(IReadOnlyList<NewsItemConfig> news)
    {
        var config = TestGameConfig.BuildWithNews(news);
        var teamId = Ulid.NewUlid();

        return GameSession.StartWithEndTurn(
            config, "test", endTurn: 999,
            new[]
            {
                new TeamSpec { Id = teamId, Name = "Команда А1", SectorId = TestGameConfig.SectorA.Id },
            });
    }

    private static void ToNextCalculation(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Calculation))
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

    [Fact]
    public void RunTick_Never_Repeats_A_Headline_Across_Multiple_Ticks()
    {
        var news = new[]
        {
            new NewsItemConfig { Id = "stable-1", Trend = EconomyTrend.Stable, Headline = "Первая новость" },
            new NewsItemConfig { Id = "stable-2", Trend = EconomyTrend.Stable, Headline = "Вторая новость" },
        };
        var session = StartSession(news);

        var published = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var appended = session.RunTick(new Random(1));
            var newsEvent = appended.SingleOrDefault(e => e.Change is NewsPublished);
            if (newsEvent is not null)
            {
                published.Add(((NewsPublished)newsEvent.Change).NewsItemId);
            }

            ToNextCalculation(session);
        }

        Assert.Equal(2, published.Distinct().Count()); // оба заголовка прозвучали, ни один не повторился

        // Пул на этот тренд исчерпан — третий тик не публикует новость вовсе (не повторяет силой).
        var thirdTickAppended = session.RunTick(new Random(1));
        Assert.DoesNotContain(thirdTickAppended, e => e.Change is NewsPublished);
    }

    [Fact]
    public void PublishManualNews_Publishes_A_Specific_Item_Regardless_Of_Trend_And_Blocks_Future_Reuse()
    {
        // TrendScenario пуст -> текущий тренд Stable, но ведущий вручную публикует заголовок Down.
        var news = new[]
        {
            new NewsItemConfig { Id = "down-1", Trend = EconomyTrend.Down, Headline = "Обвал цен на нефть" },
        };
        var session = StartSession(news);

        var entry = session.PublishManualNews("down-1");

        var published = Assert.IsType<NewsPublished>(entry.Change);
        Assert.Equal(EconomyTrend.Down, published.Trend);
        Assert.True(session.State.NewsFeed.IsPublished("down-1"));

        // Тот же заголовок нельзя опубликовать вручную повторно...
        Assert.Throws<InvalidOperationException>(() => session.PublishManualNews("down-1"));

        // ...и автоматический подбор (даже если бы тренд совпал) тоже его больше не выберет: пул общий.
        var selected = NewsCalculator.SelectNext(new[] { news[0] }, session.State.NewsFeed, EconomyTrend.Down, new Random(1));
        Assert.Null(selected);
    }

    [Fact]
    public void PublishManualNews_Throws_For_An_Unknown_Item_Id()
    {
        var session = StartSession(Array.Empty<NewsItemConfig>());

        Assert.Throws<ArgumentException>(() => session.PublishManualNews("does-not-exist"));
    }
}
