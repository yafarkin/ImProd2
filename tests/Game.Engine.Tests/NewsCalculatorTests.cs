using Game.Config.Economy;
using Game.Config.News;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Подбор новостной ленты по тренду (Блок 6.3, SPEC §5.4, §13).</summary>
public class NewsCalculatorTests
{
    private static readonly EconomyTrendPhaseConfig[] TrendScenario =
    {
        new() { Trend = EconomyTrend.Up, StartTurn = 1, EndTurn = 5, PriceChangePerTurn = 1m, CapacityChangePerTurn = 1m },
        new() { Trend = EconomyTrend.Down, StartTurn = 6, EndTurn = 10, PriceChangePerTurn = -1m, CapacityChangePerTurn = -1m },
    };

    [Fact]
    public void CurrentTrend_Follows_The_Scenario_Phase_Covering_The_Turn()
    {
        Assert.Equal(EconomyTrend.Up, NewsCalculator.CurrentTrend(3, TrendScenario));
        Assert.Equal(EconomyTrend.Down, NewsCalculator.CurrentTrend(8, TrendScenario));
    }

    [Fact]
    public void CurrentTrend_Defaults_To_Stable_Outside_Any_Scenario_Phase()
    {
        Assert.Equal(EconomyTrend.Stable, NewsCalculator.CurrentTrend(20, TrendScenario));
        Assert.Equal(EconomyTrend.Stable, NewsCalculator.CurrentTrend(1, Array.Empty<EconomyTrendPhaseConfig>()));
    }

    [Fact]
    public void SelectNext_Only_Considers_Items_Matching_The_Current_Trend()
    {
        var library = new[]
        {
            new NewsItemConfig { Id = "up-1", Trend = EconomyTrend.Up, Headline = "Рост" },
            new NewsItemConfig { Id = "down-1", Trend = EconomyTrend.Down, Headline = "Спад" },
        };
        var feed = new NewsFeed();

        var selected = NewsCalculator.SelectNext(library, feed, EconomyTrend.Up, new Random(1));

        Assert.Equal("up-1", selected!.Id);
    }

    [Fact]
    public void SelectNext_Never_Repeats_An_Already_Published_Item()
    {
        var library = new[]
        {
            new NewsItemConfig { Id = "up-1", Trend = EconomyTrend.Up, Headline = "Первый" },
            new NewsItemConfig { Id = "up-2", Trend = EconomyTrend.Up, Headline = "Второй" },
        };
        var feed = new NewsFeed();
        var random = new Random(1);

        var seen = new HashSet<string>();
        for (var i = 0; i < library.Length; i++)
        {
            var selected = NewsCalculator.SelectNext(library, feed, EconomyTrend.Up, random);
            Assert.NotNull(selected);
            Assert.True(seen.Add(selected!.Id)); // ни разу не повторился
            feed.Record(selected.Id);
        }
    }

    [Fact]
    public void SelectNext_Returns_Null_When_The_Trend_Pool_Is_Exhausted()
    {
        var library = new[] { new NewsItemConfig { Id = "up-1", Trend = EconomyTrend.Up, Headline = "Единственный" } };
        var feed = new NewsFeed();
        feed.Record("up-1");

        var selected = NewsCalculator.SelectNext(library, feed, EconomyTrend.Up, new Random(1));

        Assert.Null(selected);
    }
}
