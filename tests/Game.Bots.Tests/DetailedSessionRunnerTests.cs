using Game.Config.Loading;

namespace Game.Bots.Tests;

/// <summary>
/// <see cref="DetailedSessionRunner"/> — источник данных для <c>/admin/balance-lab</c> (Game.Web,
/// TODO.md №28, rebalance/2-sector-stepwise, 2026-08-23). Проверяет, что структурированный разбор
/// (снимок по команде на каждый ход) даёт те же числа, что и построчный текстовый разбор
/// <c>TraceRun.TraceCumulativeExpenses</c>, которым он вдохновлён, — не побитово (тот пишет текст,
/// этот — decimal), а по ключевым инвариантам: снимков ровно (ходов × команд), баланс/score меняются
/// по ходу партии, есть хоть одна строка трассировки решений бота.
/// </summary>
public class DetailedSessionRunnerTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "debug-minimal.json");
    private static string SessionPath => Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "pilot.json");

    [Fact]
    public void Run_Produces_One_Snapshot_Per_Team_Per_Turn_With_Nonempty_Trace()
    {
        var config = GameConfigLoader.LoadFromFiles(ConfigPath, SessionPath);
        var duration = config.Raw.Duration;
        const int teamsPerSector = 1;

        var result = DetailedSessionRunner.Run(config, duration.MaxTurns, teamsPerSector, maintainFactories: true, leverage: 1m, profile: 0m);

        var expectedTeamCount = config.Sectors.Count * teamsPerSector;
        Assert.Equal(expectedTeamCount * duration.MaxTurns, result.Snapshots.Count);
        Assert.NotEmpty(result.TraceLines);
        Assert.Equal(config.Sectors.Count, result.IdealHall.Branches.Count);

        // Баланс не может оставаться неизменным всю партию — хоть что-то должно происходить
        // (постройка первых фабрик, если ничего больше) в любой рабочей цепочке.
        foreach (var teamName in result.Snapshots.Select(s => s.TeamName).Distinct())
        {
            var balances = result.Snapshots.Where(s => s.TeamName == teamName).Select(s => s.Balance).ToList();
            Assert.True(balances.Distinct().Count() > 1, $"Баланс команды '{teamName}' не менялся всю партию.");
        }
    }

    [Fact]
    public void Snapshots_Are_Ordered_By_Turn_Starting_From_One()
    {
        var config = GameConfigLoader.LoadFromFiles(ConfigPath, SessionPath);
        var duration = config.Raw.Duration;

        var result = DetailedSessionRunner.Run(config, duration.MaxTurns, teamsPerSector: 1, maintainFactories: true, leverage: 1m, profile: 0m);

        var turns = result.Snapshots.Select(s => s.Turn).Distinct().OrderBy(t => t).ToList();
        Assert.Equal(1, turns.First());
        Assert.Equal(duration.MaxTurns, turns.Last());
    }

    /// <summary>
    /// Каждая строка вида <c>[sectorId] ...</c> в сыром журнале должна попасть РОВНО в одну из семи
    /// категорий <see cref="DetailedSessionRunner.TurnDecisionLog"/> — если завтра у <see
    /// cref="SimpleBot"/> появится новая формулировка (или изменится текущая), этот тест первым
    /// покраснеет вместо того, чтобы строка молча пропала из пошагового просмотра в /admin/balance-lab.
    /// </summary>
    [Fact]
    public void Every_Bracketed_Trace_Line_Is_Classified_Into_Exactly_One_Decision_Category()
    {
        var config = GameConfigLoader.LoadFromFiles(ConfigPath, SessionPath);
        var duration = config.Raw.Duration;

        var result = DetailedSessionRunner.Run(config, duration.MaxTurns, teamsPerSector: 1, maintainFactories: true, leverage: 1m, profile: 0m);

        var bracketedLineCount = result.TraceLines.Count(line => line.StartsWith('[') && line.Contains(']'));
        var classifiedLineCount = result.DecisionLogs.Sum(log =>
            log.FinancialTrend.Count + log.Build.Count + log.InvestmentPace.Count +
            log.Overhaul.Count + log.Sell.Count + log.Buy.Count + log.SystemSale.Count);

        Assert.True(bracketedLineCount > 0, "В трассировке нет ни одной строки решения бота — тест ничего не проверяет.");
        Assert.Equal(bracketedLineCount, classifiedLineCount);
    }

    /// <summary>
    /// Регрессия 2026-08-23: <c>DetailedSessionRunner</c> (в отличие от <c>--mode trace</c> в
    /// Game.Balancing) никогда не писал строк-заголовков <c>=== TURN N ===</c> — первая версия
    /// разбора решений по ходам полагалась именно на них и в итоге складывала решения ВСЕЙ партии в
    /// одну корзину «ход 0». Проверяет ход в явном виде: логов должно быть больше одного на сектор
    /// (не «всё слиплось»), и номера ходов внутри одного сектора должны реально различаться, а не все
    /// быть равны нулю/одному значению.
    /// </summary>
    [Fact]
    public void Decision_Logs_Are_Split_Across_Multiple_Distinct_Turns_Not_Collapsed_Into_One()
    {
        var config = GameConfigLoader.LoadFromFiles(ConfigPath, SessionPath);
        var duration = config.Raw.Duration;

        var result = DetailedSessionRunner.Run(config, duration.MaxTurns, teamsPerSector: 1, maintainFactories: true, leverage: 1m, profile: 0m);

        foreach (var sectorId in result.DecisionLogs.Select(l => l.SectorId).Distinct())
        {
            var turnsForSector = result.DecisionLogs.Where(l => l.SectorId == sectorId).Select(l => l.Turn).Distinct().ToList();
            Assert.True(turnsForSector.Count > 1, $"Сектор '{sectorId}': все решения попали в {turnsForSector.Count} ход(ов) — похоже на слипшуюся разбивку.");
            Assert.DoesNotContain(0, turnsForSector); // ходы нумеруются с 1 (см. Snapshots_Are_Ordered_By_Turn_Starting_From_One)
        }
    }
}
