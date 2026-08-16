using System.Globalization;
using System.Text;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>
/// Строит разреженный ряд экономической истории сессии по ходам для user-промпта LLM-бота — прямой
/// ответ на риск №1 из обсуждения TODO #20 (полная история 90 ходов не влезает в контекст-окно) и на
/// прямой запрос пользователя (2026-08-16): «баланс по ходам (и наш, и общий), история выпуска по
/// каждой фабрике, история остатка на складах» — этого не было в <see cref="BotStateSnapshotBuilder"/>
/// (тот — только срез *текущего* хода) и не входит в <see cref="BotTurnHistory"/> (та — история
/// решений самого бота, не экономики сессии).
/// <para>
/// Свёртка — не тренд одной строкой (пользователь выбрал именно ряд точек, не сводку), а
/// прореживание: точка на каждый <c>sampleInterval</c>-й ход плюс первый и текущий ход, вместо всех
/// ходов подряд — держит размер промпта ограниченным независимо от длины сессии, ценой пропуска
/// промежуточных колебаний между точками (полная свёртка с сохранением причинности каждого
/// колебания — так и остаётся нерешённой частью риска №1, это лишь первый, самый простой шаг).
/// </para>
/// Переиспользует <see cref="FactoryHistoryCalculator"/> — тот же источник данных, что уже строит
/// графики на <c>/team</c> и большом экране в Game.Web, восстановленный проигрыванием журнала.
/// </summary>
public static class BotHistorySeriesBuilder
{
    /// <summary>Строит блок разреженной истории для команды <paramref name="teamId"/>.</summary>
    public static string Build(GameSession session, Ulid teamId, int sampleInterval = 5)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (sampleInterval < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleInterval), sampleInterval, "Must sample at least every turn.");
        }

        var state = session.State;
        if (!state.Teams.TryGetValue(teamId, out var team))
        {
            throw new ArgumentException($"Unknown team '{teamId}'.", nameof(teamId));
        }

        var sampleTurns = SelectSampleTurns(state.CurrentTurn, sampleInterval);
        var ownHistory = FactoryHistoryCalculator.Summarize(session.Entries, state.Config, teamId);

        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine($"=== HISTORY (sampled turns: {string.Join(", ", sampleTurns)}) ===");

        AppendOwnNetWorth(text, ownHistory, sampleTurns);
        AppendAllTeamsNetWorth(text, session, sampleTurns);
        AppendFactoryOutput(text, team, ownHistory, sampleTurns);
        AppendWarehouseStock(text, ownHistory, sampleTurns);

        return text.ToString();
    }

    private static void AppendOwnNetWorth(StringBuilder text, FactoryHistoryCalculator.TeamFactoryHistory history, IReadOnlyList<int> sampleTurns)
    {
        text.AppendLine();
        text.AppendLine("YOUR NET WORTH BY TURN (balance - debt)");
        text.AppendLine(RenderSeries(sampleTurns, history.NetWorthByTurn.ToDictionary(p => p.Turn, p => p.NetWorth), Money));
    }

    private static void AppendAllTeamsNetWorth(StringBuilder text, GameSession session, IReadOnlyList<int> sampleTurns)
    {
        text.AppendLine();
        text.AppendLine("ALL TEAMS' NET WORTH BY TURN (big screen)");

        foreach (var team in session.State.Teams.Values.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var history = FactoryHistoryCalculator.Summarize(session.Entries, session.State.Config, team.Id);
            var byTurn = history.NetWorthByTurn.ToDictionary(p => p.Turn, p => p.NetWorth);
            text.AppendLine($"- {team.Name}: {RenderSeries(sampleTurns, byTurn, Money)}");
        }
    }

    private static void AppendFactoryOutput(StringBuilder text, Domain.Team team, FactoryHistoryCalculator.TeamFactoryHistory history, IReadOnlyList<int> sampleTurns)
    {
        text.AppendLine();
        text.AppendLine("YOUR FACTORY OUTPUT BY TURN (units produced)");

        if (history.OutputByFactoryId.Count == 0)
        {
            text.AppendLine("(no production yet)");
            return;
        }

        foreach (var (factoryId, series) in history.OutputByFactoryId.OrderBy(kv => kv.Key))
        {
            var label = team.Factories.FirstOrDefault(f => f.Id == factoryId)?.Definition.Id ?? factoryId.ToString();
            var byTurn = series.ToDictionary(p => p.Turn, p => p.OutputQuantity);
            text.AppendLine($"- {label}: {RenderSeries(sampleTurns, byTurn, Quantity)}");
        }
    }

    private static void AppendWarehouseStock(StringBuilder text, FactoryHistoryCalculator.TeamFactoryHistory history, IReadOnlyList<int> sampleTurns)
    {
        text.AppendLine();
        text.AppendLine("YOUR WAREHOUSE STOCK BY TURN");

        if (history.StockByMaterialId.Count == 0)
        {
            text.AppendLine("(no stock history yet)");
            return;
        }

        foreach (var (materialId, series) in history.StockByMaterialId.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var byTurn = series.ToDictionary(p => p.Turn, p => p.Quantity);
            text.AppendLine($"- {materialId}: {RenderSeries(sampleTurns, byTurn, Quantity)}");
        }
    }

    /// <summary>Первый ход, каждый <paramref name="interval"/>-й ход и текущий ход — без дублей, по возрастанию.</summary>
    private static IReadOnlyList<int> SelectSampleTurns(int currentTurn, int interval)
    {
        var turns = new SortedSet<int> { 1 };
        for (var turn = interval; turn <= currentTurn; turn += interval)
        {
            turns.Add(turn);
        }

        turns.Add(currentTurn);
        return turns.Where(turn => turn >= 1 && turn <= currentTurn).ToList();
    }

    /// <summary>"1: 0.00 | 5: 120.00" — точки без совпадения в ряду пропускаются (сырые данные и правда прерывистые, например у фабрики, простаивавшей без рабочих).</summary>
    private static string RenderSeries<TValue>(IReadOnlyList<int> sampleTurns, IReadOnlyDictionary<int, TValue> byTurn, Func<TValue, string> format)
    {
        var points = sampleTurns
            .Where(byTurn.ContainsKey)
            .Select(turn => $"{turn}: {format(byTurn[turn])}");

        var rendered = string.Join(" | ", points);
        return rendered.Length == 0 ? "(no data)" : rendered;
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Quantity(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
