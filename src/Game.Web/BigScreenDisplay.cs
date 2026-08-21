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
    /// Рейтинг команд по балансу, по убыванию — единственная публичная денежная метрика (SPEC §7:
    /// «финансовый рейтинг» публичен, конкретные сделки — нет).
    /// </summary>
    public static IReadOnlyList<TeamRatingRow> RankTeams(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.State.Teams.Values
            .Select(team =>
            {
                var reputation = session.GetReputation(team.Id);
                return new TeamRatingRow(
                    team.Name, team.Sector.Name, team.Balance,
                    reputation.Percentage, reputation.SampleCount);
            })
            .OrderByDescending(row => row.NetWorth)
            .ToList();
    }

    /// <summary>
    /// График баланса всех команд по ходам для большого экрана (запрос ведущего: «видеть динамику
    /// всех команд сразу», а не только свою на /team) — та же <see cref="FactoryHistoryCalculator"/>,
    /// что уже строит персональный график на /team, просто по одному ряду на каждую команду сессии
    /// вместо одной; та же величина, что и «Финансовый рейтинг» в таблице выше (<see
    /// cref="RankTeams"/>). Команды упорядочены по имени (не по текущему рейтингу — рейтинг меняется
    /// от хода к ходу, и перестановка рядов сбивала бы закреплённый за командой цвет).
    /// </summary>
    public static LineChartDiagram.ChartLayout BuildBalanceHistoryChart(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var teams = session.State.Teams.Values.OrderBy(team => team.Name, StringComparer.Ordinal).ToList();
        var series = teams
            .Select((team, index) =>
            {
                var history = FactoryHistoryCalculator.Summarize(session.Entries, session.State.Config, team.Id);
                return new LineChartDiagram.ChartSeries(
                    team.Name, SectorColors.Palette[index % SectorColors.Palette.Length], history.NetWorthByTurn);
            })
            .ToList();

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 900, 320, DashboardDisplay.FormatMoney);
    }

    /// <summary>
    /// Живой остаток дневной ёмкости сырья в рамках текущего хода (запрос пользователя: «видеть на
    /// большом экране, как крупная продажа роняет ёмкость в реальном времени»). Ось X —
    /// <see cref="MarketCapacityHistoryCalculator"/> отдаёт секунды с начала хода, не номер хода —
    /// поле <c>Turn</c> в <see cref="LineChartDiagram.ChartSeries"/> используется тут просто как
    /// обобщённая числовая ось, без изменений в самом графике. Материал без продаж в текущем ходу
    /// получает единственную точку «100% на начало хода», чтобы линия не пропадала из легенды.
    ///
    /// **Временно не вызывается из UI (Блок 9.3):** после переноса `SellToSystem` на фазу расчёта
    /// (SPEC §4) продажи и сбрасывающий график `MarketUpdated` попадают в журнал в одном и том же
    /// атомарном <see cref="GameSession.RunTick"/> — снаружи (в том числе с большого экрана) физически
    /// нельзя увидеть журнал «между» ними, только целиком до или целиком после тика. Метод оставлен —
    /// его чистая логика верна для любой последовательности событий, которую ей дадут, — но сейчас её
    /// неоткуда взять живой; требует отдельной live-превью по ещё не применённым `MaterialSaleRequested`,
    /// а не по уже применённым продажам, см. `docs/SPEC.md` §16.
    /// </summary>
    public static LineChartDiagram.ChartLayout BuildMarketCapacityChart(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var rawMaterials = session.State.Config.Materials.Values
            .Where(material => material.IsRawMaterial)
            .OrderBy(material => material.Name, StringComparer.Ordinal)
            .ToList();
        var pointsByMaterialId = MarketCapacityHistoryCalculator.SummarizeCurrentTurn(session.Entries, session.State.Config);
        var series = rawMaterials
            .Select((material, index) => new LineChartDiagram.ChartSeries(
                material.Name, SectorColors.Palette[index % SectorColors.Palette.Length],
                pointsByMaterialId.GetValueOrDefault(material.Id, [(0, 100m)])))
            .ToList();

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, 900, 320, value => value.ToString("0") + "%");
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
