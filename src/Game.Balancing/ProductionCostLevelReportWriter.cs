using System.Globalization;
using System.Text;

namespace Game.Balancing;

/// <summary>
/// Форматирует результат <see cref="ProductionCostLevelCalculator.Calculate"/> в читаемый текстовый
/// отчёт: подробно, по каждой паре (фабрика, рецепт), сгруппированной по сектору и уровню — что
/// потребляет, что производит, из чего складываются расходы и какая получилась себестоимость единицы
/// (тот же смысл, что в интерфейсе — «Себестоимость единицы», см. <c>DashboardDisplay.FormatUnitCost</c>
/// в Game.Web, здесь не переиспользуется напрямую, чтобы не тянуть зависимость на Game.Web ради двух
/// форматных строк), и наивную (без ёмкости рынка) оценку выручки/прибыли — проверка гипотезы «стоит
/// ли вообще качаться до конца цепочки» (SPEC-история про подкову/иголку/пружину), см. doc-comment
/// <see cref="ProductionCostLevelCalculator.FactoryRecipeCost.NaiveBasePrice"/>. В конце — сводная
/// таблица «сектор;уровень;сумма расходов уровня» с отметкой, где разброс между секторами на одном
/// уровне превышает <see cref="LevelParityWarningRatio"/>.
/// </summary>
public static class ProductionCostLevelReportWriter
{
    /// <summary>Порог «стоит отметить» разброса между самым дорогим и самым дешёвым сектором на одном уровне (запрос пользователя — не больше 15%).</summary>
    public const decimal LevelParityWarningRatio = 1.15m;

    public static string Format(IReadOnlyList<ProductionCostLevelCalculator.FactoryRecipeCost> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        var workers = rows.Count > 0 ? rows[0].Workers : 0;
        text.AppendLine($"Себестоимость производства при {workers} рабочих на каждой фабрике (без хода, без рынка, без зарплаты — SPEC-независимый аналитический срез).");
        text.AppendLine();

        foreach (var sectorGroup in rows.GroupBy(r => (r.SectorId, r.SectorName)).OrderBy(g => g.Key.SectorId, StringComparer.Ordinal))
        {
            text.AppendLine($"=== Сектор {sectorGroup.Key.SectorId} ({sectorGroup.Key.SectorName}) ===");
            text.AppendLine();

            foreach (var levelGroup in sectorGroup.GroupBy(r => r.Level).OrderBy(g => g.Key))
            {
                text.AppendLine($"--- Уровень {levelGroup.Key} ---");
                text.AppendLine();

                foreach (var row in levelGroup.OrderBy(r => r.FactoryId, StringComparer.Ordinal).ThenBy(r => r.RecipeId, StringComparer.Ordinal))
                {
                    text.AppendLine($"Фабрика: {row.FactoryName} [{row.FactoryId}] (рецепт: {row.RecipeId})");
                    text.AppendLine($"  Рабочих: {row.Workers}");
                    text.AppendLine($"  Выпуск: {row.OutputMaterialId} × {FormatQuantity(row.OutputQuantity)} /ход");

                    if (row.Inputs.Count == 0)
                    {
                        text.AppendLine("  Входы: (сырьё, входов нет)");
                    }
                    else
                    {
                        text.AppendLine("  Входы:");
                        foreach (var input in row.Inputs)
                        {
                            text.AppendLine(
                                $"    {input.MaterialId} × {FormatQuantity(input.Quantity)} " +
                                $"(себестоимость единицы {FormatUnitCost(input.UnitCost)}) = {FormatMoney(input.LineCost)}");
                        }
                        text.AppendLine($"    Сумма себестоимостей единиц входов: {FormatUnitCost(row.Inputs.Sum(i => i.UnitCost))}");
                    }

                    text.AppendLine("  Расходы:");
                    text.AppendLine($"    Сырьё:         {FormatMoney(row.InputCost)}");
                    text.AppendLine($"    Содержание:    {FormatMoney(row.FixedCostPerTurn)}");
                    text.AppendLine($"    Электричество: {FormatMoney(row.ElectricityCost)}");
                    text.AppendLine($"    ИТОГО:         {FormatMoney(row.TotalCost)}");
                    text.AppendLine($"  Себестоимость единицы {row.OutputMaterialId}: {FormatUnitCost(row.UnitCost)}");
                    text.AppendLine($"  Итого по фабрике: {FormatMoney(row.TotalCost)} /ход");

                    if (row.NaiveRevenue is { } naiveRevenue && row.NaiveProfit is { } naiveProfit)
                    {
                        var naiveProfitPerUnit = row.OutputQuantity > 0 ? naiveProfit / row.OutputQuantity : 0m;
                        text.AppendLine(
                            $"  Оценка выручки (БЕЗ учёта ёмкости рынка/тренда — верхняя граница, не прогноз!): " +
                            $"{FormatUnitCost(row.NaiveBasePrice!.Value)} базовая цена × {row.NaiveMarginMultiplier:0.##} маржа уровня × " +
                            $"{FormatQuantity(row.OutputQuantity)} выпуск = {FormatMoney(naiveRevenue)}");
                        text.AppendLine(
                            $"  Оценка прибыли (та же оговорка): {FormatMoney(naiveRevenue)} − {FormatMoney(row.TotalCost)} = " +
                            $"{FormatMoney(naiveProfit)} /ход ({FormatUnitCost(naiveProfitPerUnit)} на единицу)");
                        text.AppendLine(
                            $"  Прибыль на 1 рабочего: {FormatMoney(row.NaiveProfitPerWorker!.Value)} /ход " +
                            $"(при {row.Workers} рабочих на фабрике — честная мера для сравнения фабрик/уровней между собой)");
                    }
                    else
                    {
                        text.AppendLine($"  Оценка выручки/прибыли: нет базовой цены для {row.OutputMaterialId} в BaseMarketPerMaterial.");
                    }

                    if (row.Inputs.Count > 0)
                    {
                        text.AppendLine($"  Сырьё рекурсивно, на 1 ед. {row.OutputMaterialId} (до самых первых фабрик, включая межотраслевые связи):");
                        foreach (var raw in row.RawMaterialsPerUnit.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                        {
                            text.AppendLine($"    {raw.Key} × {FormatQuantity(raw.Value)}");
                        }
                        text.AppendLine($"    Сумма (физических единиц сырья, не ¤): {FormatQuantity(row.RawMaterialsPerUnit.Values.Sum())}");
                    }

                    text.AppendLine();
                }

                var levelTotalCost = levelGroup.Sum(r => r.TotalCost);
                var levelUnitCostSum = levelGroup.Sum(r => r.UnitCost);
                text.AppendLine($"Итого на уровень {levelGroup.Key}: {FormatMoney(levelTotalCost)} /ход, сумма себестоимостей единиц: {FormatUnitCost(levelUnitCostSum)}");
                var levelNaiveProfitRows = levelGroup.Where(r => r.NaiveProfit.HasValue).ToList();
                if (levelNaiveProfitRows.Count > 0)
                {
                    var levelNaiveProfitSum = levelNaiveProfitRows.Sum(r => r.NaiveProfit!.Value);
                    var levelWorkersSum = levelNaiveProfitRows.Sum(r => r.Workers);
                    var levelProfitPerWorker = levelWorkersSum > 0 ? levelNaiveProfitSum / levelWorkersSum : 0m;
                    text.AppendLine($"Оценка прибыли на уровень {levelGroup.Key} (БЕЗ учёта ёмкости рынка!): {FormatMoney(levelNaiveProfitSum)} /ход");
                    text.AppendLine($"Прибыль на 1 рабочего, уровень {levelGroup.Key}: {FormatMoney(levelProfitPerWorker)} /ход (всего {levelWorkersSum} рабочих на {levelNaiveProfitRows.Count} фабрик(ах))");
                }
                text.AppendLine();
            }
        }

        AppendLevelSummary(text, rows);
        AppendByLevelBySectorView(text, rows);
        return text.ToString();
    }

    /// <summary>
    /// Тот же итог по уровням, что и <see cref="AppendLevelSummary"/>, но перегруппированный — сначала
    /// уровень, внутри него все секторы — так удобнее сравнивать отрасли глазами на одном уровне, не
    /// листая сектора по отдельности (запрос пользователя).
    /// </summary>
    private static void AppendByLevelBySectorView(StringBuilder text, IReadOnlyList<ProductionCostLevelCalculator.FactoryRecipeCost> rows)
    {
        text.AppendLine("=== По уровням, по секторам ===");
        text.AppendLine();

        var byLevel = rows
            .GroupBy(r => r.Level)
            .OrderBy(g => g.Key);

        foreach (var levelGroup in byLevel)
        {
            text.AppendLine($"Уровень {levelGroup.Key}");
            var bySector = levelGroup
                .GroupBy(r => (r.SectorId, r.SectorName))
                .Select(g =>
                {
                    var profitRows = g.Where(r => r.NaiveProfit.HasValue).ToList();
                    var naiveProfit = profitRows.Count > 0 ? (decimal?)profitRows.Sum(r => r.NaiveProfit!.Value) : null;
                    var workersSum = profitRows.Sum(r => r.Workers);
                    var profitPerWorker = naiveProfit.HasValue && workersSum > 0 ? (decimal?)(naiveProfit.Value / workersSum) : null;
                    return (g.Key.SectorId, g.Key.SectorName, Total: g.Sum(r => r.TotalCost), NaiveProfit: naiveProfit, NaiveProfitPerWorker: profitPerWorker);
                })
                .OrderBy(x => x.SectorId, StringComparer.Ordinal);

            foreach (var entry in bySector)
            {
                var profitSuffix = entry.NaiveProfit.HasValue
                    ? $", оценка прибыли {FormatMoney(entry.NaiveProfit.Value)} /ход, {FormatMoney(entry.NaiveProfitPerWorker!.Value)} на рабочего (без ёмкости рынка!)"
                    : "";
                text.AppendLine($"    Отрасль {entry.SectorId} ({entry.SectorName}) - {FormatMoney(entry.Total)} /ход{profitSuffix}");
            }

            text.AppendLine();
        }
    }

    /// <summary>
    /// Сводная таблица «сектор;уровень;сумма ИТОГО по всем (фабрика,рецепт) этого уровня» — сумма N
    /// цифр уровня (запрос пользователя: несколько фабрик/рецептов уровня складываются в одну), плюс
    /// предупреждение там, где разброс между секторами на одном уровне превышает <see cref="LevelParityWarningRatio"/>.
    /// </summary>
    private static void AppendLevelSummary(StringBuilder text, IReadOnlyList<ProductionCostLevelCalculator.FactoryRecipeCost> rows)
    {
        text.AppendLine("=== Сводная таблица: сектор;уровень;сумма расходов уровня ===");
        text.AppendLine("sector;level;total_cost");

        var bySectorAndLevel = rows
            .GroupBy(r => (r.SectorId, r.Level))
            .Select(g => (g.Key.SectorId, g.Key.Level, Total: g.Sum(r => r.TotalCost)))
            .OrderBy(x => x.SectorId, StringComparer.Ordinal)
            .ThenBy(x => x.Level)
            .ToList();

        foreach (var entry in bySectorAndLevel)
        {
            text.AppendLine($"{entry.SectorId};{entry.Level};{entry.Total.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        text.AppendLine();
        text.AppendLine($"=== Паритет между секторами по уровням (порог предупреждения — разброс > {LevelParityWarningRatio - 1:P0}) ===");
        foreach (var levelGroup in bySectorAndLevel.GroupBy(x => x.Level).OrderBy(g => g.Key))
        {
            var values = levelGroup.ToList();
            if (values.Count < 2)
            {
                continue;
            }

            var max = values.Max(v => v.Total);
            var min = values.Min(v => v.Total);
            var ratio = min > 0 ? max / min : decimal.MaxValue;
            var marker = ratio > LevelParityWarningRatio ? " ⚠ ПРЕВЫШЕНИЕ ПОРОГА" : "";
            text.AppendLine($"  Уровень {levelGroup.Key}: разброс {ratio:0.00}×{marker}");
        }

        var maxLevel = rows.Max(r => r.Level);
        var finalLevelValues = bySectorAndLevel.Where(x => x.Level == maxLevel).ToList();
        if (finalLevelValues.Count >= 2)
        {
            var max = finalLevelValues.Max(v => v.Total);
            var min = finalLevelValues.Min(v => v.Total);
            var ratio = min > 0 ? max / min : decimal.MaxValue;
            text.AppendLine();
            text.AppendLine($"Финальный уровень цепочки ({maxLevel}): разброс между секторами {ratio:0.0000}× " +
                             (ratio <= 1.005m ? "— сходится (в пределах 0.5%)." : "— НЕ сходится (цель — не более 0.5%)."));
        }
    }

    private static string FormatMoney(decimal amount) => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} ¤";

    private static string FormatUnitCost(decimal amount) => $"{amount.ToString("0.####", CultureInfo.InvariantCulture)} ¤";

    private static string FormatQuantity(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
