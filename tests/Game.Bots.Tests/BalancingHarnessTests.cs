namespace Game.Bots.Tests;

/// <summary>Харнесс балансировки (Блок 7.2, BUILD_PLAN «Фаза 7»): метрики по прогону N партий на пилотном конфиге.</summary>
public class BalancingHarnessTests
{
    [Fact]
    public void RunSession_Collects_A_Turn_Metric_Per_Turn_And_A_Final_Score_Per_Team()
    {
        var config = PilotBotSession.LoadConfig();
        var (session, bots) = PilotBotSession.StartEightBotSession(config, endTurn: 15);

        var metrics = BalancingHarness.RunSession(session, bots, new Random(1));

        Assert.Equal(15, metrics.Turns.Count); // одна запись на каждый досчитанный ход
        Assert.Equal(Enumerable.Range(1, 15), metrics.Turns.Select(t => t.Turn));
        Assert.Equal(8, metrics.TeamCount);
        Assert.Equal(8, metrics.FinalScores.Count);
        Assert.Contains(metrics.Turns, t => t.VolumeSoldToSystem > 0); // боты действительно продают
    }

    [Fact]
    public void RunMany_Aggregates_Several_Sessions_Of_Different_Length_Into_One_Report()
    {
        var config = PilotBotSession.LoadConfig();

        var report = BalancingHarness.RunMany(3, i =>
        {
            var (session, bots) = PilotBotSession.StartEightBotSession(config, endTurn: 15 + i); // 15, 16, 17 ходов
            return (session, bots, new Random(i + 1));
        });

        Assert.Equal(3, report.SessionCount);
        Assert.Equal(17, report.TurnsByIndex.Count); // самая длинная партия — 17 ходов
        Assert.Equal(3, report.TurnsByIndex[0].SessionCount); // ход 1 — дожили все три партии
        Assert.Equal(1, report.TurnsByIndex[^1].SessionCount); // ход 17 — только самая длинная партия
        Assert.True(report.AverageFinalScoreSpread >= 0m);
    }
}
