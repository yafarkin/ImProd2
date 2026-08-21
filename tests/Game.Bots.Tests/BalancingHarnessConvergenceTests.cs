using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Прокладка идеального зала в харнесс балансировки (Блок 7.3.5) на реальной сессии, не синтетических
/// метриках — за чистой арифметикой самой агрегации см. <see cref="BalancingReportConvergenceTests"/>.
/// </summary>
public class BalancingHarnessConvergenceTests
{
    [Fact]
    public void RunSession_Leaves_Convergence_Null_Without_An_Ideal_Hall()
    {
        var config = PilotBotSession.LoadConfig();
        var (session, bots) = PilotBotSession.StartEightBotSession(config, endTurn: 15);

        var metrics = BalancingHarness.RunSession(session, bots, new Random(1));

        Assert.All(metrics.Turns, turn => Assert.Null(turn.AverageConvergence));
        Assert.Empty(metrics.FinalConvergenceBySector);
    }

    [Fact(Skip = "pilot.json требует перекалибровки после перехода на себестоимость вместо рыночной котировки, docs/TODO.md #26")]
    public void RunSession_Populates_Convergence_When_An_Ideal_Hall_Is_Given()
    {
        var config = PilotBotSession.LoadConfig();
        var (session, bots) = PilotBotSession.StartEightBotSession(config, endTurn: 15);
        var idealHall = IdealHallCalculator.Calculate(config, maxTurns: 20); // "short" пресета — MaxTurns

        var metrics = BalancingHarness.RunSession(session, bots, new Random(1), idealHall);

        Assert.Contains(metrics.Turns, turn => turn.AverageConvergence.HasValue);
        Assert.NotEmpty(metrics.FinalConvergenceBySector);
        Assert.All(metrics.FinalConvergenceBySector.Keys, sectorId => Assert.Contains(sectorId, new[] { "A", "B" }));
    }
}
