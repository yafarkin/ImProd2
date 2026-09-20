namespace Game.Bots;

/// <summary>
/// Сводка по прогону N партий (Блок 7.2, BUILD_PLAN «Харнесс балансировки»): денежная масса и
/// throughput по ходам (усреднённые по партиям — для графика роста) и средний разброс итоговых
/// счётов между командами одной партии. Плюс сходимость к идеальному залу (Блок 7.3.5,
/// <c>docs/balancing-bots.md</c> §3) — если он был передан на вход прогона.
/// </summary>
public sealed record BalancingReport
{
    /// <summary>Сколько партий вошло в сводку.</summary>
    public required int SessionCount { get; init; }

    /// <summary>Метрики по ходам, усреднённые по всем партиям, которые до каждого из них дожили.</summary>
    public required IReadOnlyList<AggregatedTurnMetrics> TurnsByIndex { get; init; }

    /// <summary>Средний по партиям разброс (максимум минус минимум) итоговых счётов команд.</summary>
    public required decimal AverageFinalScoreSpread { get; init; }

    /// <summary>
    /// Доля команд-ходов, на которых хотя бы одна фабрика пересекла критический порог износа и ушла
    /// в вынужденный простой (SPEC §5.6).
    /// </summary>
    public required decimal ForcedRepairEventShare { get; init; }

    /// <summary>
    /// Сходимость к идеальному залу на конец партии (Блок 7.3.5), по сектору, усреднённая по всем
    /// партиям сводки, — показывает, какая именно ветка систематически отстаёт в этой ячейке сетки
    /// («Готово когда» блока 7.3.5). Пусто, если ни одна партия сводки не запускалась с идеальным
    /// залом на входе.
    /// </summary>
    public required IReadOnlyDictionary<string, decimal> AverageFinalConvergenceBySector { get; init; }

    /// <summary>
    /// Средний по партиям разброс (максимум минус минимум) сходимости Score(T)/X(T) между секторами
    /// одной партии (<c>docs/balancing-bots.md</c> §3, «Итоговая сходимость между ветками») — должен
    /// быть узким у откалиброванной цепочки. <c>null</c>, если ни у одной партии не было хотя бы двух
    /// секторов со сходимостью (нечего сравнивать) или идеального зала на входе вовсе.
    /// </summary>
    public decimal? AverageFinalConvergenceSpread { get; init; }

    /// <summary>
    /// Сходимость к идеальному залу, усреднённая по всем командам и всем партиям сводки сразу, без
    /// разбивки по сектору, — одно число на ячейку сетки для тепловой карты <c>leverage×profile</c>
    /// (<c>docs/balancing-bots.md</c> §3, «Тепловая карта по сетке»). <c>null</c> на тех же условиях,
    /// что <see cref="AverageFinalConvergenceSpread"/>.
    /// </summary>
    public decimal? OverallAverageFinalConvergence { get; init; }

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

            var convergenceValues = atThisTurn.Where(t => t.AverageConvergence.HasValue).Select(t => t.AverageConvergence!.Value).ToList();

            turnsByIndex.Add(new AggregatedTurnMetrics
            {
                Turn = turn,
                AverageTotalCash = atThisTurn.Average(t => t.TotalCash),
                AverageVolumeSoldToSystem = atThisTurn.Average(t => t.VolumeSoldToSystem),
                SessionCount = atThisTurn.Count,
                AverageFactoryCondition = atThisTurn.Average(t => t.AverageFactoryCondition),
                AverageFactoriesUnderRepairCount = atThisTurn.Average(t => (decimal)t.FactoriesUnderRepairCount),
                AverageConvergence = convergenceValues.Count > 0 ? convergenceValues.Average() : null,
            });
        }

        var totalTeamTurns = sessions.Sum(session => session.Turns.Count * session.TeamCount);

        var totalForcedRepairs = sessions.Sum(session => session.Turns.Sum(turn => turn.ForcedRepairEventsCount));
        var forcedRepairEventShare = totalTeamTurns > 0 ? (decimal)totalForcedRepairs / totalTeamTurns : 0m;

        var averageFinalScoreSpread = sessions.Average(session =>
        {
            var scores = session.FinalScores.Select(f => f.Score).ToList();
            return scores.Count > 0 ? scores.Max() - scores.Min() : 0m;
        });

        var averageFinalConvergenceBySector = sessions
            .SelectMany(session => session.FinalConvergenceBySector)
            .GroupBy(entry => entry.Key)
            .ToDictionary(group => group.Key, group => group.Average(entry => entry.Value));

        var spreadsPerSession = sessions
            .Where(session => session.FinalConvergenceBySector.Count >= 2)
            .Select(session => session.FinalConvergenceBySector.Values.Max() - session.FinalConvergenceBySector.Values.Min())
            .ToList();
        var averageFinalConvergenceSpread = spreadsPerSession.Count > 0 ? spreadsPerSession.Average() : (decimal?)null;

        var allConvergenceValues = sessions.SelectMany(session => session.FinalConvergenceBySector.Values).ToList();
        var overallAverageFinalConvergence = allConvergenceValues.Count > 0 ? allConvergenceValues.Average() : (decimal?)null;

        return new BalancingReport
        {
            SessionCount = sessions.Count,
            TurnsByIndex = turnsByIndex,
            AverageFinalScoreSpread = averageFinalScoreSpread,
            ForcedRepairEventShare = forcedRepairEventShare,
            AverageFinalConvergenceBySector = averageFinalConvergenceBySector,
            AverageFinalConvergenceSpread = averageFinalConvergenceSpread,
            OverallAverageFinalConvergence = overallAverageFinalConvergence,
        };
    }
}
