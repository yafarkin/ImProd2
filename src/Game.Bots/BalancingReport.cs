namespace Game.Bots;

/// <summary>
/// Сводка по прогону N партий (Блок 7.2, BUILD_PLAN «Харнесс балансировки»): денежная масса и
/// throughput по ходам (усреднённые по партиям — для графика роста), доля дефолтов (принудительных
/// займов на команду-ход) и средний разброс итоговых счётов между командами одной партии.
/// </summary>
public sealed record BalancingReport
{
    /// <summary>Сколько партий вошло в сводку.</summary>
    public required int SessionCount { get; init; }

    /// <summary>Метрики по ходам, усреднённые по всем партиям, которые до каждого из них дожили.</summary>
    public required IReadOnlyList<AggregatedTurnMetrics> TurnsByIndex { get; init; }

    /// <summary>Доля команд-ходов, закончившихся принудительным займом, — прокси «дефолта» (своего понятия банкротства в игре нет).</summary>
    public required decimal ForcedLoanShare { get; init; }

    /// <summary>Средний по партиям разброс (максимум минус минимум) итоговых счётов команд.</summary>
    public required decimal AverageFinalScoreSpread { get; init; }

    /// <summary>
    /// Доля команд-ходов, на которых хотя бы одна фабрика пересекла критический порог износа и ушла
    /// в вынужденный простой (SPEC §5.6) — тот же приём, что <see cref="ForcedLoanShare"/>, для новой
    /// механики.
    /// </summary>
    public required decimal ForcedRepairEventShare { get; init; }

    public static BalancingReport Summarize(IReadOnlyList<SessionMetrics> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count == 0)
        {
            throw new ArgumentException("At least one session is required.", nameof(sessions));
        }

        var allTurns = sessions.SelectMany(session => session.Turns).ToList();
        var maxTurn = allTurns.Count == 0 ? 0 : allTurns.Max(turn => turn.Turn);

        var turnsByIndex = new List<AggregatedTurnMetrics>();
        for (var turn = 1; turn <= maxTurn; turn++)
        {
            var atThisTurn = allTurns.Where(t => t.Turn == turn).ToList();
            if (atThisTurn.Count == 0)
            {
                continue;
            }

            turnsByIndex.Add(new AggregatedTurnMetrics
            {
                Turn = turn,
                AverageTotalCash = atThisTurn.Average(t => t.TotalCash),
                AverageVolumeSoldToSystem = atThisTurn.Average(t => t.VolumeSoldToSystem),
                SessionCount = atThisTurn.Count,
                AverageFactoryCondition = atThisTurn.Average(t => t.AverageFactoryCondition),
                AverageFactoriesUnderRepairCount = atThisTurn.Average(t => (decimal)t.FactoriesUnderRepairCount),
            });
        }

        var totalForcedLoans = sessions.Sum(session => session.Turns.Sum(turn => turn.ForcedLoanCount));
        var totalTeamTurns = sessions.Sum(session => session.Turns.Count * session.TeamCount);
        var forcedLoanShare = totalTeamTurns > 0 ? (decimal)totalForcedLoans / totalTeamTurns : 0m;

        var totalForcedRepairs = sessions.Sum(session => session.Turns.Sum(turn => turn.ForcedRepairEventsCount));
        var forcedRepairEventShare = totalTeamTurns > 0 ? (decimal)totalForcedRepairs / totalTeamTurns : 0m;

        var averageFinalScoreSpread = sessions.Average(session =>
        {
            var scores = session.FinalScores.Select(f => f.Score).ToList();
            return scores.Count > 0 ? scores.Max() - scores.Min() : 0m;
        });

        return new BalancingReport
        {
            SessionCount = sessions.Count,
            TurnsByIndex = turnsByIndex,
            ForcedLoanShare = forcedLoanShare,
            AverageFinalScoreSpread = averageFinalScoreSpread,
            ForcedRepairEventShare = forcedRepairEventShare,
        };
    }
}
