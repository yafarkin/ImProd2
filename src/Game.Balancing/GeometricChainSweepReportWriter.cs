using System.Globalization;
using System.Text;

namespace Game.Balancing;

/// <summary>
/// Форматирует <see cref="GeometricChainSweep.Run"/> в читаемую ASCII-карту (строки — коэффициент
/// роста BuildCost, столбцы — коэффициент спада ProductionRate) — направление C плана исследований
/// (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>, 2026-08-24). Каждая ячейка — ✅ (все
/// уровни окупаются) или номер первого уровня, на котором цепочка ломается — читается как
/// "докуда можно углублять цепочку при таком сочетании рычагов", не просто да/нет.
/// </summary>
public static class GeometricChainSweepReportWriter
{
    public static string Format(IReadOnlyList<GeometricChainSweep.CellResult> results, int levels, decimal paybackWarningTurns)
    {
        ArgumentNullException.ThrowIfNull(results);

        var text = new StringBuilder();
        text.AppendLine(
            $"Направление C — геометрическая развёртка (глубина цепочки {levels}, порог окупаемости {paybackWarningTurns:F0} ход(ов)).");
        text.AppendLine(
            "Ячейка: ✅ — вся цепочка окупается на каждом уровне; иначе — номер первого (самого мелкого) провалившегося уровня.");
        text.AppendLine();

        var growths = results.Select(r => r.BuildCostGrowth).Distinct().OrderBy(g => g).ToList();
        var decays = results.Select(r => r.ProductionRateDecay).Distinct().OrderByDescending(d => d).ToList();
        var byCell = results.ToDictionary(r => (r.BuildCostGrowth, r.ProductionRateDecay));

        const int columnWidth = 7;
        text.Append("growth\\decay".PadRight(14));
        foreach (var decay in decays)
        {
            text.Append(decay.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(columnWidth));
        }
        text.AppendLine();

        foreach (var growth in growths)
        {
            text.Append(growth.ToString("0.00", CultureInfo.InvariantCulture).PadRight(14));
            foreach (var decay in decays)
            {
                var cell = byCell[(growth, decay)];
                var mark = cell.AllLevelsViable ? "✅" : cell.FirstFailingLevel!.Value.ToString(CultureInfo.InvariantCulture);
                text.Append(mark.PadLeft(columnWidth));
            }

            text.AppendLine();
        }

        text.AppendLine();
        var viableCount = results.Count(r => r.AllLevelsViable);
        text.AppendLine($"Жизнеспособных сочетаний: {viableCount} из {results.Count} ({(decimal)viableCount / results.Count:P0}).");

        return text.ToString();
    }
}
