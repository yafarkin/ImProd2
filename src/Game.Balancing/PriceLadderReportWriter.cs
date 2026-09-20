using System.Text;
using Game.Config.Economy;

namespace Game.Balancing;

/// <summary>
/// Форматирует лестницу цен (<see cref="SystemSalePriceLadderCalculator.Calculate"/>) в текстовый
/// отчёт «уровень / себестоимость / цена / маржа» по каждому сектору — выход блока 11.2
/// (<c>docs/external-economy.md</c> §9).
///
/// <para>Главная колонка отчёта — <b>прибыль с единицы</b>, а не наценка: наценка растёт с уровнем
/// по построению (в этом и смысл <c>DepthBonus</c>), поэтому сама по себе ничего не доказывает.
/// Вопрос, ради которого инструмент существует, звучит иначе — <i>окупает ли глубокий передел свою
/// стройку</i>, а на него отвечают абсолютные деньги за ход, не проценты.</para>
/// </summary>
internal static class PriceLadderReportWriter
{
    public static string Format(
        IReadOnlyList<SystemSalePriceLadderCalculator.MaterialLadderRow> rows, decimal baseMargin, decimal depthBonusPerLevel)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        text.AppendLine(
            $"Лестница цен сбыта: цена = себестоимость × (1 + {baseMargin:P0} + {depthBonusPerLevel:P0} × уровень).");
        if (depthBonusPerLevel == 0m)
        {
            text.AppendLine(
                "  DepthBonus = 0 — лестница в точности воспроизводит прежнее правило cost-plus (одна наценка на все уровни).");
        }

        text.AppendLine();

        foreach (var sector in rows.Select(r => r.SectorId).Distinct(StringComparer.Ordinal))
        {
            text.AppendLine($"=== Сектор {sector} ===");
            text.AppendLine($"{"Ур",3}  {"Материал",-24} {"Себестоимость",14} {"Цена",12} {"Маржа",8} {"Прибыль/ед",12}  {"Было",10}");

            foreach (var row in rows.Where(r => string.Equals(r.SectorId, sector, StringComparison.Ordinal)))
            {
                text.AppendLine(
                    $"{row.Level,3}  {Truncate(row.MaterialName, 24),-24} {row.UnitCost,14:N4} {row.NewPrice,12:N4} " +
                    $"{row.MarginRate,8:P0} {row.ProfitPerUnit,12:N4}  {row.OldPrice,10:N2}");
            }

            text.AppendLine();
        }

        AppendDepthSummary(text, rows);
        return text.ToString();
    }

    /// <summary>
    /// Сводка «вознаграждается ли глубина»: во сколько раз прибыль с единицы на самом глубоком
    /// уровне сектора больше, чем на сырье. При <c>DepthBonus = 0</c> это отношение равно отношению
    /// себестоимостей — то есть глубина не вознаграждается сверх того, что в неё вложено, и ровно
    /// это и было структурным дефектом cost-plus (<c>docs/economy-accounting-audit.md</c>).
    /// </summary>
    private static void AppendDepthSummary(
        StringBuilder text, IReadOnlyList<SystemSalePriceLadderCalculator.MaterialLadderRow> rows)
    {
        text.AppendLine("=== Вознаграждение за глубину (прибыль с единицы: самый глубокий уровень против сырья) ===");

        foreach (var sector in rows.Select(r => r.SectorId).Distinct(StringComparer.Ordinal))
        {
            var sectorRows = rows.Where(r => string.Equals(r.SectorId, sector, StringComparison.Ordinal)).ToList();
            var shallowest = sectorRows.OrderBy(r => r.Level).First();
            var deepest = sectorRows.OrderByDescending(r => r.Level).First();

            var ratio = shallowest.ProfitPerUnit > 0m
                ? (deepest.ProfitPerUnit / shallowest.ProfitPerUnit).ToString("N1") + "×"
                : "н/д";

            text.AppendLine(
                $"  {sector}: уровень {shallowest.Level} → {deepest.Level}, " +
                $"прибыль/ед {shallowest.ProfitPerUnit:N4} → {deepest.ProfitPerUnit:N4} ({ratio})");
        }

        text.AppendLine();
        text.AppendLine(
            "Отношение выше — про единицу продукции, не про ход: глубокие уровни выпускают кратно меньше единиц " +
            "(ёмкость рынка падает вдоль цепочки). Окончательный ответ «окупается ли глубина» даёт --mode diagnose §1b " +
            "после применения лестницы, а не этот отчёт.");
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";
}
