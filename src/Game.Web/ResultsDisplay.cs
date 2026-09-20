using Game.Engine;

namespace Game.Web;

/// <summary>
/// Данные экрана итогов игры (docs/TODO.md №10, SPEC §12) — по тому же принципу, что и
/// <see cref="BigScreenDisplay"/>: чистые статические функции над уже посчитанным состоянием сессии,
/// без собственного хранимого состояния.
///
/// <para>
/// Счёт (<see cref="FinalScoreCalculator"/>) считается всю игру и годится в любой момент, но здесь
/// он впервые показывается человеку: до этого его можно было получить только выгрузкой
/// <c>/export/scores.csv</c>. Разбивка «касса / склад / фабрики» — не украшение, а весь смысл
/// экрана: одинаковый счёт у двух команд может быть набран противоположными способами, и разговор
/// на разборе идёт именно про это.
/// </para>
/// </summary>
public static class ResultsDisplay
{
    /// <summary>
    /// Строка итоговой таблицы. <see cref="Place"/> — место с учётом дележа: две команды с равным
    /// счётом делят место, следующая за ними получает место со сдвигом (1, 2, 2, 4), как в спорте.
    /// </summary>
    public sealed record ResultRow(
        int Place, Ulid TeamId, string Name, string SectorName,
        decimal Cash, decimal WarehouseValue, decimal FactoriesValue, decimal Score,
        decimal ReputationPercentage, int ReputationSampleCount);

    /// <summary>
    /// Итоговая таблица по убыванию счёта. В отличие от рейтинга большого экрана
    /// (<see cref="BigScreenDisplay.RankTeams"/>, где сортировка идёт по сырому балансу — публичной
    /// метрике по ходу игры, SPEC §7) здесь сортировка по полному счёту: партия кончилась, скрывать
    /// больше нечего, и вложенные в фабрики деньги обязаны быть засчитаны.
    /// </summary>
    public static IReadOnlyList<ResultRow> Rank(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var state = session.State;
        var materialCosts = MaterialCostCalculator.CalculateAll(state.Config);
        var scored = state.Teams.Values
            .Select(team =>
            {
                var score = FinalScoreCalculator.Calculate(team, materialCosts, state.Config.Raw.FactoryDefinitions);
                var reputation = session.GetReputation(team.Id);
                return (Team: team, Score: score, Reputation: reputation);
            })
            .OrderByDescending(entry => entry.Score.Score)
            // Тай-брейк по имени — только чтобы порядок строк не плавал между обновлениями страницы;
            // на само место он не влияет, равный счёт остаётся дележом места.
            .ThenBy(entry => entry.Team.Name, StringComparer.Ordinal)
            .ToList();

        var rows = new List<ResultRow>(scored.Count);
        for (var index = 0; index < scored.Count; index++)
        {
            var entry = scored[index];
            var place = index > 0 && scored[index - 1].Score.Score == entry.Score.Score
                ? rows[index - 1].Place
                : index + 1;

            rows.Add(new ResultRow(
                place, entry.Team.Id, entry.Team.Name, entry.Team.Sector.Name,
                entry.Score.Cash, entry.Score.WarehouseValue, entry.Score.FactoriesValue, entry.Score.Score,
                entry.Reputation.Percentage, entry.Reputation.SampleCount));
        }

        return rows;
    }

    /// <summary>
    /// График счёта всех команд по ходам — главная картинка разбора: видно не только кто выиграл, но
    /// и на каком ходу разошлись траектории. Команды упорядочены по имени и берут цвета из той же
    /// палитры в том же порядке, что и график баланса на большом экране
    /// (<see cref="BigScreenDisplay.BuildBalanceHistoryChart"/>), — за командой держится один цвет на
    /// всех экранах, иначе после часа игры зал будет искать свою линию заново.
    /// </summary>
    public static LineChartDiagram.ChartLayout BuildScoreHistoryChart(GameSession session, double width = 900, double height = 320)
    {
        ArgumentNullException.ThrowIfNull(session);

        var teams = session.State.Teams.Values.OrderBy(team => team.Name, StringComparer.Ordinal).ToList();
        var series = teams
            .Select((team, index) =>
            {
                var history = FactoryHistoryCalculator.Summarize(session.Entries, session.State.Config, team.Id);
                return new LineChartDiagram.ChartSeries(
                    team.Name, SectorColors.Palette[index % SectorColors.Palette.Length], history.ScoreByTurn);
            })
            .ToList();

        return LineChartDiagram.Build(series, LineChartDiagram.ChartScale.Linear, width, height, DashboardDisplay.FormatMoney);
    }
}
