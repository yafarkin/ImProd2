using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Агрегация сходимости к идеальному залу (Блок 7.3.5, <c>docs/balancing-bots.md</c> §3) — чистая
/// арифметика <see cref="BalancingReport.Summarize"/> на заранее собранных <see cref="SessionMetrics"/>,
/// без реальной сессии/ботов/идеального зала (те уже проверены в <see cref="BalancingHarnessConvergenceTests"/>).
/// </summary>
public class BalancingReportConvergenceTests
{
    private static SessionMetrics BuildSession(
        IReadOnlyDictionary<string, decimal> finalConvergenceBySector, params decimal?[] turnConvergence)
    {
        var turns = turnConvergence.Select((convergence, index) => new TurnMetrics
        {
            Turn = index + 1,
            TotalCash = 0m,
            VolumeSoldToSystem = 0m,
            AverageFactoryCondition = 1m,
            FactoriesUnderRepairCount = 0,
            ForcedRepairEventsCount = 0,
            AverageConvergence = convergence,
        }).ToList();

        return new SessionMetrics
        {
            Turns = turns,
            FinalScores = new List<FinalScoreResult>
            {
                new() { TeamId = Ulid.NewUlid(), Cash = 0m, WarehouseValue = 0m, FactoriesValue = 0m, Score = 0m },
            },
            TeamCount = 1,
            FinalConvergenceBySector = finalConvergenceBySector,
        };
    }

    [Fact]
    public void Summarize_Leaves_Convergence_Fields_Empty_Without_Any_Ideal_Hall_Data()
    {
        var session = BuildSession(new Dictionary<string, decimal>(), null, null);

        var report = BalancingReport.Summarize(new[] { session });

        Assert.Empty(report.AverageFinalConvergenceBySector);
        Assert.Null(report.AverageFinalConvergenceSpread);
        Assert.Null(report.OverallAverageFinalConvergence);
        Assert.All(report.TurnsByIndex, turn => Assert.Null(turn.AverageConvergence));
    }

    [Fact]
    public void Summarize_Averages_Final_Convergence_Per_Sector_Across_Sessions_And_The_Spread_Between_Them()
    {
        var sessionA = BuildSession(new Dictionary<string, decimal> { ["A"] = 0.8m, ["B"] = 0.4m }, 0.5m);
        var sessionB = BuildSession(new Dictionary<string, decimal> { ["A"] = 1.0m, ["B"] = 0.6m }, 0.7m);

        var report = BalancingReport.Summarize(new[] { sessionA, sessionB });

        Assert.Equal(0.9m, report.AverageFinalConvergenceBySector["A"]); // (0.8+1.0)/2
        Assert.Equal(0.5m, report.AverageFinalConvergenceBySector["B"]); // (0.4+0.6)/2
        Assert.Equal(0.7m, report.OverallAverageFinalConvergence); // (0.8+0.4+1.0+0.6)/4 — плоское среднее по всем (партия, сектор)
        Assert.Equal(0.4m, report.AverageFinalConvergenceSpread); // ((0.8-0.4)+(1.0-0.6))/2 — средний разброс А/Б внутри каждой партии
        Assert.Equal(0.6m, report.TurnsByIndex.Single().AverageConvergence); // (0.5+0.7)/2 — временной ряд для дебрифа
    }

    [Fact]
    public void Summarize_Skips_Sessions_With_Fewer_Than_Two_Sectors_When_Computing_The_Spread()
    {
        // Разброс между ветками не считается, если сравнивать не с чем, — но общая сходимость всё
        // равно доступна.
        var singleSector = BuildSession(new Dictionary<string, decimal> { ["A"] = 0.5m });

        var report = BalancingReport.Summarize(new[] { singleSector });

        Assert.Null(report.AverageFinalConvergenceSpread);
        Assert.Equal(0.5m, report.OverallAverageFinalConvergence);
    }
}
