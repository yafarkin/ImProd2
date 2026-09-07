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
    public void SelectNext_Prefers_Items_That_Have_Not_Been_Published_Yet()
    {
        var library = new[]
        {
            new NewsItemConfig { Id = "up-1", Trend = EconomyTrend.Up, Headline = "Первый" },
            new NewsItemConfig { Id = "up-2", Trend = EconomyTrend.Up, Headline = "Второй" },
        };
        var feed = new NewsFeed();
        var random = new Random(1);

        var seen = new HashSet<string>();
        for (var turn = 1; turn <= library.Length; turn++)
        {
            var selected = NewsCalculator.SelectNext(library, feed, EconomyTrend.Up, random);
            Assert.NotNull(selected);
            Assert.True(seen.Add(selected!.Id)); // пока в пуле есть свежие — повторов нет
            feed.Record(selected.Id, turn);
        }
    }

    /// <summary>
    /// Блок 11.9: исчерпание пула больше не означает тишину до конца партии — берётся самый давно
    /// не звучавший заголовок тренда.
    /// </summary>
    [Fact]
    public void SelectNext_Reuses_The_Least_Recently_Published_Item_When_The_Pool_Is_Exhausted()
    {
        var library = new[]
        {
            new NewsItemConfig { Id = "up-1", Trend = EconomyTrend.Up, Headline = "Первый" },
            new NewsItemConfig { Id = "up-2", Trend = EconomyTrend.Up, Headline = "Второй" },
            new NewsItemConfig { Id = "up-3", Trend = EconomyTrend.Up, Headline = "Третий" },
        };
        var feed = new NewsFeed();
        feed.Record("up-1", 5);
        feed.Record("up-2", 3);
        feed.Record("up-3", 9);

        // up-2 звучал давнее всех; up-3 звучал последним и потому исключён из кандидатов вовсе.
        var selected = NewsCalculator.SelectNext(library, feed, EconomyTrend.Up, new Random(1));

        Assert.Equal("up-2", selected!.Id);
    }

    /// <summary>
    /// Тишина остаётся возможной ровно в одном случае — заголовков этого тренда нет в библиотеке
    /// вовсе. Это дыра в контенте, а не исчерпание пула, и молча подставлять чужой тренд нельзя:
    /// новость соврала бы о том, к чему готовиться.
    /// </summary>
    [Fact]
    public void SelectNext_Returns_Null_Only_When_The_Trend_Has_No_Headlines_At_All()
    {
        var library = new[] { new NewsItemConfig { Id = "up-1", Trend = EconomyTrend.Up, Headline = "Единственный" } };
        var feed = new NewsFeed();

        Assert.Null(NewsCalculator.SelectNext(library, feed, EconomyTrend.Down, new Random(1)));
    }

    /// <summary>
    /// Пул из одного заголовка — единственное место, где повтор подряд допустим: выбора нет, а
    /// молчащая лента хуже повторившейся.
    /// </summary>
    [Fact]
    public void SelectNext_Repeats_A_Single_Item_Pool_Rather_Than_Falling_Silent()
    {
        var library = new[] { new NewsItemConfig { Id = "up-1", Trend = EconomyTrend.Up, Headline = "Единственный" } };
        var feed = new NewsFeed();
        feed.Record("up-1", 1);

        Assert.Equal("up-1", NewsCalculator.SelectNext(library, feed, EconomyTrend.Up, new Random(1))!.Id);
    }

    /// <summary>
    /// Опережение: заголовок хода <c>t</c> описывает тренд хода <c>t + lookahead</c> — на ходу 3 при
    /// опережении 3 лента уже предупреждает о спаде, начинающемся на ходу 6.
    /// </summary>
    [Fact]
    public void ForecastTrend_Describes_The_Trend_That_Will_Be_In_Effect_Later()
    {
        Assert.Equal(EconomyTrend.Down, NewsCalculator.ForecastTrend(3, 3, TrendScenario));
        Assert.Equal(EconomyTrend.Up, NewsCalculator.ForecastTrend(1, 1, TrendScenario));

        // Нулевое опережение — прежнее поведение «лента как хроника».
        Assert.Equal(NewsCalculator.CurrentTrend(3, TrendScenario), NewsCalculator.ForecastTrend(3, 0, TrendScenario));
    }

    [Fact]
    public void ForecastTrend_Rejects_A_Negative_Lookahead()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewsCalculator.ForecastTrend(3, -1, TrendScenario));
    }
}
