using System.Globalization;
using System.Text;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// CSV-форматирование сводок дебрифа (Блок 10.1, SPEC §12) — тот же формат, что уже использует
/// <c>Game.Balancing</c> для сводок по симуляциям: запятая, английские PascalCase заголовки,
/// <see cref="CultureInfo.InvariantCulture"/> для чисел.
/// </summary>
public static class CsvExport
{
    public static string TurnsToCsv(IReadOnlyList<TurnHistoryCalculator.TurnSummary> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var sb = new StringBuilder();
        sb.AppendLine("Turn,TotalCash,VolumeSoldToSystem");
        foreach (var turn in turns)
        {
            sb.AppendLine(string.Join(',',
                turn.Turn.ToString(CultureInfo.InvariantCulture),
                turn.TotalCash.ToString(CultureInfo.InvariantCulture),
                turn.VolumeSoldToSystem.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    public static string ScoresToCsv(IReadOnlyList<(string TeamName, FinalScoreResult Score)> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        var sb = new StringBuilder();
        sb.AppendLine("TeamName,Cash,WarehouseValue,FactoriesValue,Score");
        foreach (var (teamName, score) in scores)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(teamName),
                score.Cash.ToString(CultureInfo.InvariantCulture),
                score.WarehouseValue.ToString(CultureInfo.InvariantCulture),
                score.FactoriesValue.ToString(CultureInfo.InvariantCulture),
                score.Score.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    /// <summary>Команду мог назвать администратор произвольным текстом (Блок 9.8) — экранируем на случай запятой/кавычки/переноса строки.</summary>
    private static string EscapeCsv(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}
