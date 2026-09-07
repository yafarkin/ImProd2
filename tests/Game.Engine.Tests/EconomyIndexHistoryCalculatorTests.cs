using Game.Config.Economy;
using Game.Config.News;

namespace Game.Engine.Tests;

/// <summary>
/// Восстановление истории индекса деловой активности из журнала (блок 11.3) — данные для графика
/// «состояние внешней экономики». Проверяется главное: история берётся из того, что реально
/// произошло, и не заглядывает за последнюю запись журнала.
/// </summary>
public class EconomyIndexHistoryCalculatorTests
{
    private static readonly IReadOnlyList<EconomyTrendPhaseConfig> Upswing =
    [
        new EconomyTrendPhaseConfig
        {
            Trend = EconomyTrend.Up,
            StartTurn = 1,
            EndTurn = 90,
            PriceChangePerTurn = 0m,
            CapacityChangePerTurn = 0m,
            IndexChangePerTurn = 0.01m,
        },
    ];

    /// <summary>Конфиг с непрерывным подъёмом на всю партию — индекс растёт на 0.01 каждый ход.</summary>
    private static Game.Config.Loading.ResolvedGameConfig BuildRisingEconomy() =>
        TestGameConfig.BuildWithNews(Array.Empty<NewsItemConfig>(), Upswing);

    private static void ToNextSettlement(GameSession session)
    {
        var turn = session.State.CurrentTurn;
        while (!(session.State.CurrentTurn > turn && session.State.CurrentPhase == TurnPhase.Settlement))
        {
            session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    /// <summary>До старта сессии истории нет — не пустая точка и не ноль, а именно пусто.</summary>
    [Fact]
    public void There_Is_No_History_Before_The_Session_Starts()
    {
        var points = EconomyIndexHistoryCalculator.Summarize(
            Array.Empty<EventLogEntry<GameSessionState>>(), TestGameConfig.Resolved);

        Assert.Empty(points);
    }

    /// <summary>
    /// Первый ход попадает в историю сразу при старте сессии: рынок первого хода публикуется самим
    /// <see cref="SessionStarted"/>, отдельного <see cref="MarketUpdated"/> на него нет.
    /// </summary>
    [Fact]
    public void The_First_Turn_Appears_As_Soon_As_The_Session_Starts()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        var points = EconomyIndexHistoryCalculator.Summarize(session.Entries, TestGameConfig.Resolved);

        var only = Assert.Single(points);
        Assert.Equal(1, only.Turn);
        Assert.Equal(EconomyIndexCalculator.NeutralIndex, only.Index);
    }

    /// <summary>
    /// Индекс первого хода берётся из состояния после <see cref="SessionStarted"/>, а не назначается
    /// нейтральным вслепую: сценарий, стартующий с первого же хода, обязан быть виден в первой точке.
    /// Регрессия на первую версию этого калькулятора, которая именно так и ошибалась.
    /// </summary>
    [Fact]
    public void The_First_Point_Reflects_A_Scenario_That_Starts_On_Turn_One()
    {
        var config = BuildRisingEconomy();
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam(config: config);

        var points = EconomyIndexHistoryCalculator.Summarize(session.Entries, config);

        var only = Assert.Single(points);
        Assert.Equal(1, only.Turn);
        Assert.Equal(1.01m, only.Index);
    }

    /// <summary>Каждый следующий ход добавляет ровно одну точку, в порядке возрастания хода.</summary>
    [Fact]
    public void Each_Settled_Turn_Adds_Exactly_One_Point_In_Chronological_Order()
    {
        var config = BuildRisingEconomy();
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam(config: config);

        session.RunTick(new Random(1));
        ToNextSettlement(session);
        session.RunTick(new Random(1));

        var points = EconomyIndexHistoryCalculator.Summarize(session.Entries, config);

        Assert.Equal(new[] { 1, 2 }, points.Select(p => p.Turn).ToArray());
        Assert.Equal(new[] { 1.01m, 1.02m }, points.Select(p => p.Index).ToArray());
    }

    /// <summary>
    /// История обрывается на текущем ходу и никогда не показывает будущее, хотя индекс — чистая
    /// функция от хода и весь сценарий до конца партии посчитать вперёд технически можно. Прогноз в
    /// игре — работа новостной ленты, а не графика с точными числами.
    /// </summary>
    [Fact]
    public void The_History_Never_Reaches_Beyond_The_Last_Journal_Entry()
    {
        var config = BuildRisingEconomy();
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam(config: config);

        session.RunTick(new Random(1));

        var points = EconomyIndexHistoryCalculator.Summarize(session.Entries, config);

        Assert.Equal(session.State.CurrentTurn, points[^1].Turn);
        Assert.DoesNotContain(points, p => p.Turn > session.State.CurrentTurn);
    }

    /// <summary>Один и тот же журнал всегда восстанавливается в одну и ту же историю.</summary>
    [Fact]
    public void Replaying_The_Same_Journal_Rebuilds_The_Same_History()
    {
        var config = BuildRisingEconomy();
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam(config: config);
        session.RunTick(new Random(1));

        var first = EconomyIndexHistoryCalculator.Summarize(session.Entries, config);
        var second = EconomyIndexHistoryCalculator.Summarize(session.Entries, config);

        Assert.Equal(first, second);
    }
}
