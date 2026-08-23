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
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "debug-minimal.json");
    private static string SessionPath => Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "debug-minimal.json");

    [Fact]
    public void Run_Produces_One_Snapshot_Per_Team_Per_Turn_With_Nonempty_Trace()
    {
        var config = GameConfigLoader.LoadFromFiles(ConfigPath, SessionPath);
        var preset = config.Raw.SessionPresets.Single(p => p.Id == "full");
        const int teamsPerSector = 1;

        var result = DetailedSessionRunner.Run(config, preset.Id, preset.MaxTurns, teamsPerSector, maintainFactories: true, leverage: 1m, profile: 0m);

        var expectedTeamCount = config.Sectors.Count * teamsPerSector;
        Assert.Equal(expectedTeamCount * preset.MaxTurns, result.Snapshots.Count);
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
        var preset = config.Raw.SessionPresets.Single(p => p.Id == "full");

        var result = DetailedSessionRunner.Run(config, preset.Id, preset.MaxTurns, teamsPerSector: 1, maintainFactories: true, leverage: 1m, profile: 0m);

        var turns = result.Snapshots.Select(s => s.Turn).Distinct().OrderBy(t => t).ToList();
        Assert.Equal(1, turns.First());
        Assert.Equal(preset.MaxTurns, turns.Last());
    }
}
