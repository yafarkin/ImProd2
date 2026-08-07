using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Прогоняет одну или много партий силами простых ботов (Блок 7.2, BUILD_PLAN «Харнесс
/// балансировки») и собирает метрики: денежная масса и throughput по ходам, доля принудительных
/// займов, разброс итоговых счётов — для калибровки GameConfig, не для игры вживую.
/// </summary>
public static class BalancingHarness
{
    /// <summary>Прогоняет одну партию до конца и собирает её метрики.</summary>
    public static SessionMetrics RunSession(GameSession session, IReadOnlyList<SimpleBot> bots, Random random)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(random);

        var turns = new List<TurnMetrics>();
        BotSessionRunner.RunToCompletion(session, bots, random, onTurnCompleted: appended =>
        {
            var totalCash = session.State.Teams.Values.Sum(team => team.Balance);
            var volumeSold = appended.Sum(entry => entry.Change is MaterialSoldToSystem sale ? sale.Volume : 0m);
            var forcedLoans = appended.Count(entry => entry.Change is ForcedLoanTaken);

            var allFactories = session.State.Teams.Values.SelectMany(team => team.Factories).ToList();
            var averageFactoryCondition = allFactories.Count > 0 ? allFactories.Average(factory => factory.Condition) : 1m;
            var factoriesUnderRepair = allFactories.Count(factory => factory.IsUnderRepair);
            var forcedRepairEvents = appended.Count(entry => entry.Change is FactoryEnteredRepair);

            turns.Add(new TurnMetrics
            {
                Turn = session.State.CurrentTurn,
                TotalCash = totalCash,
                VolumeSoldToSystem = volumeSold,
                ForcedLoanCount = forcedLoans,
                AverageFactoryCondition = averageFactoryCondition,
                FactoriesUnderRepairCount = factoriesUnderRepair,
                ForcedRepairEventsCount = forcedRepairEvents,
            });
        });

        var finalScores = bots
            .Select(bot => FinalScoreCalculator.Calculate(
                session.State.Teams[bot.TeamId],
                session.State.Market,
                session.State.Config.Raw.Economy,
                session.State.Config.Raw.FactoryDefinitions))
            .ToList();

        return new SessionMetrics
        {
            Turns = turns,
            FinalScores = finalScores,
            TeamCount = bots.Count,
        };
    }

    /// <summary>
    /// Прогоняет <paramref name="sessionCount"/> независимых партий — <paramref name="sessionFactory"/>
    /// собирает свежую сессию, ботов и генератор случайности для каждого прогона по его номеру
    /// (0-based), например, только меняя зерно жеребьёвки хода окончания — и сводит их метрики в
    /// один отчёт.
    /// </summary>
    public static BalancingReport RunMany(
        int sessionCount, Func<int, (GameSession Session, IReadOnlyList<SimpleBot> Bots, Random Random)> sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        if (sessionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionCount), sessionCount, "Session count must be positive.");
        }

        var results = new List<SessionMetrics>();
        for (var i = 0; i < sessionCount; i++)
        {
            var (session, bots, random) = sessionFactory(i);
            results.Add(RunSession(session, bots, random));
        }

        return BalancingReport.Summarize(results);
    }
}
