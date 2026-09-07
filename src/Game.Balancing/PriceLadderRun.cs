using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Game.Config.Economy;
using Game.Config.Loading;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Режим <c>--mode price-ladder</c> (блок 11.2, <c>docs/external-economy.md</c> §4): считает
/// экзогенные цены сбыта от себестоимости, печатает отчёт и — по явному <c>--apply</c> — записывает
/// их обратно в файл конфига.
///
/// <para><b>Запись сделана точечной правкой JSON, а не пересериализацией конфига.</b> Пересборка
/// всего файла из <c>GameConfig</c> переформатировала бы боевую модель целиком (в
/// <c>main-3-sectors.json</c> — под тысячу строк), и настоящая правка цен утонула бы в диффе, где её
/// невозможно проверить глазами. Здесь меняются ровно те числа, что должны измениться, всё остальное
/// в файле остаётся байт в байт.</para>
/// </summary>
internal static class PriceLadderRun
{
    public static void Run(ResolvedGameConfig config, CliArguments cliArguments)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(cliArguments);

        var unitCosts = MaterialCostCalculator.CalculateAll(config);
        var rows = SystemSalePriceLadderCalculator.Calculate(
            config, unitCosts, cliArguments.BaseMargin, cliArguments.DepthBonusPerLevel);

        Console.WriteLine(PriceLadderReportWriter.Format(rows, cliArguments.BaseMargin, cliArguments.DepthBonusPerLevel));

        if (!cliArguments.Apply)
        {
            Console.WriteLine("Предпросмотр — файл не изменён. Записать: повторить вызов с --apply.");
            return;
        }

        if (cliArguments.ConfigPath is not { } configPath)
        {
            throw new ArgumentException(
                "'--apply' requires an explicit '--config <path>': the tool must know which file to rewrite, " +
                "and the interactive chain picker does not identify one.");
        }

        var updatedCount = ApplyToFile(configPath, rows);
        Console.WriteLine($"Записано в {configPath}: обновлено цен — {updatedCount} из {rows.Count}.");

        if (updatedCount != rows.Count)
        {
            Console.WriteLine(
                "  ⚠ Часть материалов не найдена в BaseMarketPerMaterial файла — проверьте, что --config указывает " +
                "на ту же модель, по которой считалась лестница.");
        }
    }

    /// <summary>
    /// Точечно правит <c>BaseSellPrice</c> в <c>BaseMarketPerMaterial</c> файла. Понимает обе формы,
    /// которые умеет грузить <see cref="ConfigSelector"/>: файл production-модели (массив в корне) и
    /// уже собранный <c>GameConfig</c> (массив внутри <c>Economy</c>). Возвращает число реально
    /// обновлённых записей.
    /// </summary>
    internal static int ApplyToFile(string configPath, IReadOnlyList<SystemSalePriceLadderCalculator.MaterialLadderRow> rows)
    {
        var root = JsonNode.Parse(File.ReadAllText(configPath))
                   ?? throw new InvalidOperationException($"Config file '{configPath}' is empty or not valid JSON.");

        var market = root["BaseMarketPerMaterial"]
                     ?? root["Economy"]?["BaseMarketPerMaterial"]
                     ?? throw new InvalidOperationException(
                         $"Config file '{configPath}' has no 'BaseMarketPerMaterial' at the root nor under 'Economy'.");

        var newPriceByMaterialId = rows.ToDictionary(r => r.MaterialId, r => r.NewPrice);
        var updated = 0;

        foreach (var entry in market.AsArray())
        {
            if (entry?["MaterialId"]?.GetValue<string>() is not { } materialId
                || !newPriceByMaterialId.TryGetValue(materialId, out var newPrice))
            {
                continue;
            }

            entry["BaseSellPrice"] = JsonValue.Create(Math.Round(newPrice, 4));
            updated++;
        }

        File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return updated;
    }

    /// <summary>Строка сводки для лога вызывающего кода — сколько уровней и какой разброс маржи получился.</summary>
    public static string Summarize(IReadOnlyList<SystemSalePriceLadderCalculator.MaterialLadderRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return "лестница пуста";
        }

        var minMargin = rows.Min(r => r.MarginRate);
        var maxMargin = rows.Max(r => r.MarginRate);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"материалов: {rows.Count}, маржа {minMargin:P0}..{maxMargin:P0}");
    }
}
