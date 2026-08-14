using System.Diagnostics;
using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Внешний цикл сетки стратегий поверх <see cref="BalancingHarness"/> (Блок 7.3.2,
/// <c>docs/balancing-bots.md</c> §2): для каждой пары <c>(leverage, profile)</c> прогоняет
/// <paramref name="sessionsPerCell"/>-подобное число партий (см. <see cref="Run"/>) и сводит их в
/// обычный <see cref="BalancingReport"/> — харнесс блока 7.2 не переписан, сетка добавлена вокруг
/// него отдельным слоем, как и было запланировано.
/// </summary>
public static class StrategyGridRunner
{
    /// <summary>
    /// <paramref name="sessionFactory"/> получает <c>(leverage, profile, номер партии внутри ячейки)</c>
    /// и собирает свежую сессию с ботами, сконструированными под эти <c>leverage</c>/<c>profile</c>, —
    /// тем же приёмом, что и <see cref="BalancingHarness.RunMany"/>. <paramref name="onSessionCompleted"/>
    /// — необязательный heartbeat-колбэк (<see cref="StrategyGridProgress"/>), вызывается после каждой
    /// отдельной партии, а не только по ячейке целиком, — сетка рассчитана на часы работы, вызывающий
    /// код (обычно консоль) сам решает, как часто из этих отметок печатать строку (см.
    /// <c>Game.Balancing/Program.cs</c>). <paramref name="idealHall"/> — необязательный идеальный зал
    /// (Блок 7.3.5) для сходимости <c>Score(t)/X(t)</c>: одна и та же ссылка передаётся в каждую
    /// партию каждой ячейки — X(t) зависит только от конфига, не от <c>leverage</c>/<c>profile</c>,
    /// пересчитывать его на ячейку незачем (вызывающий код должен посчитать его один раз заранее).
    /// </summary>
    public static IReadOnlyList<StrategyGridCellResult> Run(
        IReadOnlyList<decimal> leverageLevels,
        IReadOnlyList<decimal> profileLevels,
        int sessionsPerCell,
        Func<decimal, decimal, int, (GameSession Session, IReadOnlyList<SimpleBot> Bots, Random Random)> sessionFactory,
        Action<StrategyGridProgress>? onSessionCompleted = null,
        IdealHallResult? idealHall = null)
    {
        ArgumentNullException.ThrowIfNull(leverageLevels);
        ArgumentNullException.ThrowIfNull(profileLevels);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        if (leverageLevels.Count == 0 || profileLevels.Count == 0)
        {
            throw new ArgumentException("At least one leverage level and one profile level are required.");
        }
        if (sessionsPerCell <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionsPerCell), sessionsPerCell, "Sessions per cell must be positive.");
        }

        var results = new List<StrategyGridCellResult>();
        var totalCells = leverageLevels.Count * profileLevels.Count;
        var cellIndex = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var leverage in leverageLevels)
        {
            foreach (var profile in profileLevels)
            {
                cellIndex++;
                var sessions = new List<SessionMetrics>();
                for (var sessionIndex = 0; sessionIndex < sessionsPerCell; sessionIndex++)
                {
                    var (session, bots, random) = sessionFactory(leverage, profile, sessionIndex);
                    sessions.Add(BalancingHarness.RunSession(session, bots, random, idealHall));

                    onSessionCompleted?.Invoke(new StrategyGridProgress
                    {
                        CellIndex = cellIndex,
                        TotalCells = totalCells,
                        Leverage = leverage,
                        Profile = profile,
                        SessionIndex = sessionIndex + 1,
                        SessionsPerCell = sessionsPerCell,
                        Elapsed = stopwatch.Elapsed,
                    });
                }

                results.Add(new StrategyGridCellResult
                {
                    Leverage = leverage,
                    Profile = profile,
                    Report = BalancingReport.Summarize(sessions),
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Равномерная сетка уровней 0..1 (например, <paramref name="steps"/>=5 → 0, 0.25, 0.5, 0.75, 1) —
    /// общий помощник для обеих осей, чтобы вызывающий код не дублировал арифметику шага.
    /// </summary>
    public static IReadOnlyList<decimal> UniformLevels(int steps)
    {
        if (steps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), steps, "Step count must be positive.");
        }
        if (steps == 1)
        {
            return new[] { 0m };
        }

        return Enumerable.Range(0, steps).Select(i => (decimal)i / (steps - 1)).ToList();
    }
}
