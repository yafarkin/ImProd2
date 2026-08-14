using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>Внешний цикл сетки стратегий (Блок 7.3.2) поверх <see cref="BalancingHarness"/>.</summary>
public class StrategyGridRunnerTests
{
    [Theory]
    [InlineData(1, new double[] { 0.0 })]
    [InlineData(2, new double[] { 0.0, 1.0 })]
    [InlineData(5, new double[] { 0.0, 0.25, 0.5, 0.75, 1.0 })]
    public void UniformLevels_Produces_Evenly_Spaced_Values_Between_Zero_And_One(int steps, double[] expected)
    {
        var levels = StrategyGridRunner.UniformLevels(steps);

        Assert.Equal(expected.Select(v => (decimal)v), levels);
    }

    [Fact]
    public void Run_Covers_Every_Cell_Of_The_Grid_And_Reports_Progress_For_Every_Session()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");
        var leverageLevels = new[] { 0m, 1m };
        var profileLevels = new[] { 0m, 1m };
        const int sessionsPerCell = 2;

        var progressCalls = new List<StrategyGridProgress>();
        var results = StrategyGridRunner.Run(leverageLevels, profileLevels, sessionsPerCell, (leverage, profile, sessionIndex) =>
        {
            var teams = new List<TeamSpec>();
            var bots = new List<SimpleBot>();
            for (var t = 0; t < 4; t++)
            {
                var sector = t % 2 == 0 ? sectorA : sectorB;
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"Бот {t}", SectorId = sector.Id });
                bots.Add(new SimpleBot(teamId, sector, config, leverage: leverage, profile: profile));
            }

            var session = GameSession.StartWithEndTurn(config, "short", endTurn: 10, teams);
            return (session, (IReadOnlyList<SimpleBot>)bots, new Random(sessionIndex + 1));
        }, progressCalls.Add);

        Assert.Equal(leverageLevels.Length * profileLevels.Length, results.Count);
        foreach (var leverage in leverageLevels)
        {
            foreach (var profile in profileLevels)
            {
                var cell = Assert.Single(results, c => c.Leverage == leverage && c.Profile == profile);
                Assert.Equal(sessionsPerCell, cell.Report.SessionCount);
            }
        }

        // Один вызов колбэка на каждую завершённую партию — 4 ячейки × 2 партии.
        Assert.Equal(leverageLevels.Length * profileLevels.Length * sessionsPerCell, progressCalls.Count);
        Assert.All(progressCalls, p => Assert.Equal(leverageLevels.Length * profileLevels.Length, p.TotalCells));
        Assert.Equal(Enumerable.Repeat(new[] { 1, 2 }, leverageLevels.Length * profileLevels.Length).SelectMany(x => x),
            progressCalls.Select(p => p.SessionIndex));
    }

    [Fact]
    public void Run_Threads_The_Ideal_Hall_Into_Every_Cells_Convergence_Metrics()
    {
        // Один и тот же идеальный зал (Блок 7.3.5) на все ячейки сетки — X(t) зависит только от
        // конфига, не от leverage/profile (doc-comment StrategyGridRunner.Run).
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");
        var idealHall = IdealHallCalculator.Calculate(config, maxTurns: 20);

        var results = StrategyGridRunner.Run(new[] { 0m, 1m }, new[] { 0m }, sessionsPerCell: 1, (leverage, profile, sessionIndex) =>
        {
            var teams = new List<TeamSpec>();
            var bots = new List<SimpleBot>();
            for (var t = 0; t < 4; t++)
            {
                var sector = t % 2 == 0 ? sectorA : sectorB;
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"Бот {t}", SectorId = sector.Id });
                bots.Add(new SimpleBot(teamId, sector, config, leverage: leverage, profile: profile));
            }

            var session = GameSession.StartWithEndTurn(config, "short", endTurn: 15, teams);
            return (session, (IReadOnlyList<SimpleBot>)bots, new Random(sessionIndex + 1));
        }, idealHall: idealHall);

        Assert.All(results, cell => Assert.NotNull(cell.Report.OverallAverageFinalConvergence));
    }
}
