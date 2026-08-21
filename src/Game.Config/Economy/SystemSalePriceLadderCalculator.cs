using Game.Config.Loading;
using Game.Domain;

namespace Game.Config.Economy;

/// <summary>
/// Пересчитывает <see cref="MaterialMarketConfig.BasePrice"/> по цепочке переделов так, чтобы доход
/// от продажи системе строго рос вниз по каждой цепочке (запрос пользователя, Блок 9.4: обнаружили,
/// что в отладочном конфиге доход от «осесть на сырье/железе и не перерабатывать дальше» выше, чем
/// от честной переработки до конца цепочки — <see cref="MaterialMarketConfig.BaseCapacity"/> падает
/// ровно в 10 раз на каждом переделе (задано рецептами), а <see cref="BasePrice"/> подбирался
/// вручную и растёт неравномерно, местами меньше этих 10 раз). Не трогает
/// <see cref="MaterialMarketConfig.BaseCapacity"/> — только <see cref="MaterialMarketConfig.BasePrice"/>,
/// и только у материалов, чья цепочка
/// начинается с явно заданного <paramref name="rootAnchorPrices"/> (см. <see cref="Calculate"/>) —
/// материалы вне выбранных цепочек (например, другой сектор) остаются как есть.
///
/// Это первый, узкий кирпичик к будущему «общему ползунку сложности сессии» (запрос пользователя):
/// <c>growthPerLevel</c> — именно такая именованная, воспроизводимая «ручка», а не подобранные один
/// раз вручную числа. Сам общий ползунок (стоимость построек, скорость R&amp;D и т.д. — тоже под одну
/// сложность) — отдельная, более крупная задача, не эта.
///
/// <para><b>Открытый вопрос с 2026-08-21 (rebalance/2-sector-stepwise), не решён в этом шаге:</b>
/// реальная системная цена продажи теперь — себестоимость (<c>Game.Engine.MaterialCostCalculator</c>)
/// × фиксированная наценка, <see cref="MaterialMarketConfig.BasePrice"/> в цене больше не участвует
/// вообще, только в этом инструменте предпросмотра. Инструмент не сломан (продолжает считать то, что
/// считал), но его практическая польза для реального ценообразования сейчас нулевая — решение, что с
/// ним делать (переписать на себестоимость или убрать), не принято, не в рамках сегодняшнего
/// запроса.</para>
/// </summary>
public static class SystemSalePriceLadderCalculator
{
    /// <summary>
    /// Наценка системной продажи — дублирует <c>Game.Engine.MarketSaleCalculator.SystemSaleMarginMultiplier</c>
    /// (Game.Config не может ссылаться на Game.Engine — обратное направление зависимостей), должна
    /// оставаться синхронной с ней вручную. С 2026-08-22 фиксированная и одна на все уровни передела —
    /// раньше здесь была настраиваемая таблица по уровню.
    /// </summary>
    private const decimal SystemSaleMarginMultiplier = 1.05m;

    /// <summary>Одна строка предпросмотра — материал цепочки, было/станет, для отображения администратору до применения.</summary>
    public sealed record MaterialLadderRow(
        string MaterialId,
        string MaterialName,
        int Level,
        string? PredecessorMaterialId,
        bool IsRepriced,
        decimal Capacity,
        decimal MarginMultiplier,
        decimal OldPrice,
        decimal NewPrice,
        decimal OldFullCapacityRevenue,
        decimal NewFullCapacityRevenue);

    /// <summary>
    /// Считает предпросмотр новой лестницы цен. <paramref name="rootAnchorPrices"/> — цена сырья
    /// (материалов уровня 0), с которых начинается пересчёт каждой выбранной цепочки; материал
    /// уровня 0, которого нет в этом словаре, и всё, что от него зависит, в пересчёт не попадает и
    /// остаётся с прежней ценой (<see cref="MaterialLadderRow.IsRepriced"/> = <see langword="false"/>) —
    /// так можно пересчитать одну цепочку (например, металлургию), не трогая параллельную (нефть).
    /// Для каждого следующего материала цепочки цена подбирается так, чтобы «доход при полной
    /// выборке ёмкости рынка» (<c>Capacity × Price × MarginMultiplier</c>) был ровно в
    /// <paramref name="growthPerLevel"/> раз больше, чем у материала-предшественника (единственный
    /// прямой вход рецепта, производящего этот материал, — если входов несколько, берётся первый по
    /// порядку в конфиге, остальные на форму цепочки не влияют).
    /// </summary>
    public static IReadOnlyList<MaterialLadderRow> Calculate(
        ResolvedGameConfig config, decimal growthPerLevel, IReadOnlyDictionary<string, decimal> rootAnchorPrices)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(rootAnchorPrices);
        if (growthPerLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(growthPerLevel), growthPerLevel, "Growth per level must be positive.");
        }

        var marketByMaterialId = config.Raw.Economy.BaseMarketPerMaterial.ToDictionary(m => m.MaterialId);

        var newPriceByMaterialId = new Dictionary<string, decimal>();
        var repriced = new HashSet<string>();
        var rows = new List<MaterialLadderRow>();

        // Уровень 0 гарантированно не зависит ни от кого (Recipe запрещает входы у сырья) — простой
        // проход по возрастанию Level гарантированно видит предшественника раньше потомка.
        foreach (var material in config.Materials.Values
                     .Where(m => marketByMaterialId.ContainsKey(m.Id))
                     .OrderBy(m => m.Level))
        {
            var market = marketByMaterialId[material.Id];
            string? predecessorId = null;
            decimal newPrice;

            if (material.IsRawMaterial)
            {
                if (rootAnchorPrices.TryGetValue(material.Id, out var anchor))
                {
                    newPrice = anchor;
                    repriced.Add(material.Id);
                }
                else
                {
                    newPrice = market.BasePrice;
                }
            }
            else
            {
                // Непервичный материал без рецепта — не валидный конфиг (см. проверку ссылочной
                // целостности в GameConfigLoader), но защищаемся: тихо не пересчитываем эту ветку,
                // а не роняем экран администратора обскурным KeyNotFoundException.
                var recipe = config.RecipeBook.TryGetRecipe(material);
                var predecessor = recipe?.Inputs.FirstOrDefault()?.Material;
                predecessorId = predecessor?.Id;

                if (predecessor is not null && repriced.Contains(predecessor.Id) && marketByMaterialId.TryGetValue(predecessor.Id, out var predecessorMarket))
                {
                    var predecessorNewPrice = newPriceByMaterialId[predecessor.Id];
                    newPrice = predecessorNewPrice * predecessorMarket.BaseCapacity * SystemSaleMarginMultiplier * growthPerLevel
                               / (market.BaseCapacity * SystemSaleMarginMultiplier);
                    repriced.Add(material.Id);
                }
                else
                {
                    newPrice = market.BasePrice;
                }
            }

            newPriceByMaterialId[material.Id] = newPrice;
            var thisMarginMultiplier = SystemSaleMarginMultiplier;
            rows.Add(new MaterialLadderRow(
                material.Id, material.Name, material.Level, predecessorId, repriced.Contains(material.Id),
                market.BaseCapacity, thisMarginMultiplier, market.BasePrice, newPrice,
                market.BaseCapacity * market.BasePrice * thisMarginMultiplier,
                market.BaseCapacity * newPrice * thisMarginMultiplier));
        }

        return rows;
    }

    /// <summary>
    /// Применяет предпросмотр из <see cref="Calculate"/> к конфигу: возвращает новый
    /// <see cref="GameConfig"/> с обновлённым <see cref="EconomyConfig.BaseMarketPerMaterial"/> —
    /// у материалов вне пересчитанных цепочек (<see cref="MaterialLadderRow.IsRepriced"/> = false)
    /// цена не меняется. Не валидирует и не пересобирает <see cref="ResolvedGameConfig"/> — это
    /// обязанность вызывающего кода (см. <see cref="GameConfigLoader.Load"/>), симметрично тому, как
    /// <see cref="GameConfigWriter"/> тоже только сериализует, не проверяя.
    /// </summary>
    public static GameConfig Apply(GameConfig config, IReadOnlyList<MaterialLadderRow> rows)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(rows);

        var newPriceByMaterialId = rows.Where(r => r.IsRepriced).ToDictionary(r => r.MaterialId, r => r.NewPrice);
        var newMarket = config.Economy.BaseMarketPerMaterial
            .Select(m => newPriceByMaterialId.TryGetValue(m.MaterialId, out var newPrice) ? m with { BasePrice = newPrice } : m)
            .ToList();

        return config with { Economy = config.Economy with { BaseMarketPerMaterial = newMarket } };
    }
}
