using Game.Domain;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// Агрегация данных для большого экрана (Блок 9.7, SPEC §9.1) — по тому же принципу, что и
/// <see cref="DashboardDisplay"/>/<see cref="PhaseDisplay"/>: чистые статические функции над уже
/// посчитанным состоянием сессии, без собственного хранимого состояния.
/// </summary>
public static class BigScreenDisplay
{
    /// <summary>Строка рейтинга команд большого экрана.</summary>
    public sealed record TeamRatingRow(
        string Name, string SectorName, decimal NetWorth,
        decimal ReputationPercentage, int ReputationSampleCount);

    /// <summary>
    /// Рейтинг команд по чистой стоимости (баланс минус долг), по убыванию — единственная публичная
    /// денежная метрика (SPEC §7: «финансовый рейтинг» публичен, конкретные сделки — нет).
    /// </summary>
    public static IReadOnlyList<TeamRatingRow> RankTeams(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.State.Teams.Values
            .Select(team =>
            {
                var reputation = session.GetReputation(team.Id);
                return new TeamRatingRow(
                    team.Name, team.Sector.Name, team.Balance - team.Debt,
                    reputation.Percentage, reputation.SampleCount);
            })
            .OrderByDescending(row => row.NetWorth)
            .ToList();
    }

    /// <summary>Агрегированная сводка доски потребностей для большого экрана.</summary>
    public sealed record NeedsSummary(int ActiveCount, IReadOnlyList<string> MostSoughtMaterialNames);

    /// <summary>
    /// Сводка доски потребностей: сколько активных записей и какой материал (материалы) чаще всего в
    /// дефиците (SPEC §9.1: «активных объявлений: N · чаще всего ищут: …»).
    /// </summary>
    public static NeedsSummary SummarizeNeeds(GameSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var active = state.Needs.Values.Where(n => n.Status == NeedStatus.Active).ToList();
        var deficitGroups = active
            .Where(n => n.Direction == NeedDirection.Deficit)
            .GroupBy(n => n.Material.Name)
            .ToList();
        var maxCount = deficitGroups.Count == 0 ? 0 : deficitGroups.Max(g => g.Count());
        var topNames = deficitGroups
            .Where(g => g.Count() == maxCount)
            .Select(g => g.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new NeedsSummary(active.Count, topNames);
    }
}
