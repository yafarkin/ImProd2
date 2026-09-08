using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Прокладка идеального зала в харнесс балансировки (Блок 7.3.5) на реальной сессии, не синтетических
/// метриках — за чистой арифметикой самой агрегации см. <see cref="BalancingReportConvergenceTests"/>.
/// Обе проверки идут на поставочной паре конфигов (<see cref="ShippedBotSession"/>): сходимость
/// считается относительно X(t) конкретной цепочки, и мерить её на постороннем каталоге бессмысленно.
/// Прогон короткий (15 ходов) намеренно — здесь проверяется проводка величины через харнесс, а не
/// экономический исход партии (за ним — <see cref="BotSessionRunnerTests"/>).
/// </summary>
public class BalancingHarnessConvergenceTests
{
    [Fact]
    public void RunSession_Leaves_Convergence_Null_Without_An_Ideal_Hall()
    {
        var config = ShippedBotSession.LoadConfig();
        var (session, bots) = ShippedBotSession.StartSession(config, endTurn: 15);

        var metrics = BalancingHarness.RunSession(session, bots, new Random(1));

        Assert.All(metrics.Turns, turn => Assert.Null(turn.AverageConvergence));
        Assert.Empty(metrics.FinalConvergenceBySector);
    }

    /// <summary>
    /// Здесь партия идёт целиком, в отличие от проверки выше. Сходимость определена только на ходах,
    /// где X(t) уже положителен (доля от ещё не окупившегося старта смысла не имеет, см.
    /// <see cref="BalancingHarness"/>), а боевая цепочка по построению выходит в плюс ближе к 75-му
    /// ходу — на пятнадцатом ходу метрика законно пуста, и короткий прогон проверял бы не проводку
    /// величины, а её отсутствие.
    /// </summary>
    [Fact]
    public void RunSession_Populates_Convergence_When_An_Ideal_Hall_Is_Given()
    {
        var config = ShippedBotSession.LoadConfig();
        var maxTurns = config.Raw.Duration.MaxTurns;
        var (session, bots) = ShippedBotSession.StartSession(config, maxTurns);
        var idealHall = IdealHallCalculator.Calculate(config, maxTurns);

        var metrics = BalancingHarness.RunSession(session, bots, new Random(1), idealHall);

        Assert.Contains(metrics.Turns, turn => turn.AverageConvergence.HasValue);
        Assert.NotEmpty(metrics.FinalConvergenceBySector);
        // Секторы берутся из конфига, а не перечисляются литералами: у боевой цепочки их три, и
        // список должен следовать за содержанием файла, а не за памятью автора теста.
        Assert.All(
            metrics.FinalConvergenceBySector.Keys,
            sectorId => Assert.Contains(sectorId, config.Sectors.Select(s => s.Id)));
    }
}
