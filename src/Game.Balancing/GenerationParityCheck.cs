using Game.Config.Loading;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Статическая проверка «фронт-лоадинга стартового поколения» (<c>docs/TODO.md</c> №2, находка сессии
/// 2026-08-15) — не бот, не идеальный зал, мгновенный подсчёт по одному конфигу: суммарная «ценность»
/// (<c>BaseCapacity × себестоимость × <see cref="MarketSaleCalculator.SystemSaleMarginMultiplier"/></c>,
/// себестоимость через <see cref="MaterialCostCalculator"/> — до 2026-08-22 здесь была
/// <c>BasePrice × маржа уровня</c>, но с rebalance/2-sector-stepwise <c>BasePrice</c> ни на что не
/// влияет, реальная системная цена — только себестоимость) материалов уровня 1..<see
/// cref="Game.Config.Economy.GenerationResearchConfig.StartingGeneration"/> в каждом секторе — то есть
/// то, что команда может продавать, вообще не вкладываясь в исследование поколений.
///
/// <para>
/// Важность этой конкретной проверки не гипотетическая: один и тот же баг (у одного сектора вдвое-
/// впятеро больше материалов уровня 1, чем у соседей) был найден вручную дважды подряд — сперва на
/// <c>metallurgy-petrochemistry.json</c>, затем на <c>metallurgy-petrochemistry-forestry.json</c> — оба
/// раза часами прогонов сетки ботов. Причина, почему это так сильно бьёт по деньгам, а не просто
/// «неровно»: <c>SimpleBot</c> при <c>leverage=0</c> вообще не инвестирует в исследование поколений и
/// навсегда остаётся на стартовом поколении (см. <c>SimpleBot.UpdateInvestmentPace</c>) — значит вся
/// эта асимметрия почти напрямую становится разницей реальных денег ботов при пассивной стратегии, а
/// не сглаживается за счёт остальной цепочки. Эта проверка ловит его сразу, не дожидаясь очередного
/// ручного расследования на следующей стадии.
/// </para>
/// </summary>
public static class GenerationParityCheck
{
    /// <summary>Порог «стоит предупредить» — разрыв между самым богатым и самым бедным сектором на старте.</summary>
    public const decimal WarningRatioThreshold = 1.5m;

    public static IReadOnlyList<SectorGenerationValue> Calculate(ResolvedGameConfig config)
    {
        var materialCosts = MaterialCostCalculator.CalculateAll(config);
        var marketByMaterialId = config.Raw.Economy.BaseMarketPerMaterial
            .ToDictionary(m => m.MaterialId, m => m);
        var startingGeneration = config.Raw.GenerationResearch.StartingGeneration;

        return config.Sectors.Select(sector =>
        {
            var startingMaterials = config.Materials.Values
                .Where(m => m.Sector == sector && m.Level >= 1 && m.Level <= startingGeneration)
                .ToList();

            var value = startingMaterials.Sum(m => marketByMaterialId.TryGetValue(m.Id, out var market) && materialCosts.TryGetValue(m.Id, out var cost)
                ? market.BaseCapacity * cost * MarketSaleCalculator.SystemSaleMarginMultiplier
                : 0m);

            return new SectorGenerationValue
            {
                SectorId = sector.Id,
                SectorName = sector.Name,
                MaterialCount = startingMaterials.Count,
                Value = value,
            };
        }).ToList();
    }

    /// <summary>Печатает таблицу и, если разрыв превышает <see cref="WarningRatioThreshold"/>, предупреждение — вызывается перед основным прогоном, для обоих режимов (дёшево, не зависит от бота/сессии).</summary>
    public static void PrintReport(IReadOnlyList<SectorGenerationValue> values)
    {
        if (values.Count < 2)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Паритет стартового поколения (материалы уровня 1..StartingGeneration — доступны боту с leverage=0 навсегда):");
        foreach (var value in values.OrderByDescending(v => v.Value))
        {
            Console.WriteLine($"  {value.SectorId} ({value.SectorName}): {value.MaterialCount} материалов, ценность {value.Value:N0}");
        }

        var max = values.Max(v => v.Value);
        var min = values.Min(v => v.Value);
        if (min <= 0m)
        {
            Console.WriteLine("  ПРЕДУПРЕЖДЕНИЕ: у одного из секторов на стартовом поколении вообще нет продаваемых материалов.");
            return;
        }

        var ratio = max / min;
        if (ratio > WarningRatioThreshold)
        {
            Console.WriteLine(
                $"  ПРЕДУПРЕЖДЕНИЕ: разрыв {ratio:0.00}x между самым богатым и самым бедным сектором на старте " +
                "(docs/TODO.md №2, «генерация-1 фронт-лоадинг») — почти напрямую перейдёт в разницу реальных " +
                "денег ботов при leverage=0, независимо от того, насколько сходится потолок X(T).");
        }
    }
}

/// <summary>Одна строка отчёта <see cref="GenerationParityCheck"/> — сектор, сколько материалов уровня 1..StartingGeneration и их суммарная ценность.</summary>
public sealed record SectorGenerationValue
{
    public required string SectorId { get; init; }
    public required string SectorName { get; init; }
    public required int MaterialCount { get; init; }
    public required decimal Value { get; init; }
}
