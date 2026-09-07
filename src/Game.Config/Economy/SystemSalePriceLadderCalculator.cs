using Game.Config.Loading;

namespace Game.Config.Economy;

/// <summary>
/// Считает лестницу экзогенных цен сбыта — <see cref="MaterialMarketConfig.BaseSellPrice"/> по каждому
/// материалу конфига (блок 11.2, <c>docs/external-economy.md</c> §4):
///
/// <code>
/// BaseSellPrice(M) = себестоимость(M) × (1 + BaseMargin + DepthBonus × уровень(M))
/// </code>
///
/// <para><b>Себестоимость участвует ровно один раз — здесь, при калибровке.</b> Результат
/// замораживается в конфиг обычными числами, и во время партии движок себестоимость для цены больше
/// не спрашивает (<c>Game.Engine.ExternalPriceCalculator</c>). Именно это делает цену настоящим
/// внешним якорем: команда, снизившая свою реальную себестоимость через R&amp;D, забирает разницу
/// себе, а не отдаёт её обратно в цену, как было при cost-plus.</para>
///
/// <para><b>Зачем такая форма.</b> <c>DepthBonus</c> — единственная ручка, отвечающая на вопрос
/// «насколько сильнее вознаграждается глубина передела»: при нуле цена всюду равна
/// <c>себестоимость × (1 + BaseMargin)</c>, то есть в точности воспроизводит прежнее правило
/// cost-plus, а с ростом — маржа глубоких уровней расходится с мелкими. Отсюда главное практическое
/// свойство инструмента: калибровка (блок 11.8) начинается не с нуля, а из точки, про которую уже
/// известно, что она проходима, и сводится к подъёму одной именованной ручки, а не к поиску вслепую
/// по двум цепочкам.</para>
///
/// <para><b>Что здесь было раньше.</b> До 2026-09-07 лестница считалась не от себестоимости, а от
/// <see cref="MaterialMarketConfig.BaseCapacity"/> — «подобрать цену так, чтобы доход при полной
/// выборке ёмкости рос ровно в <c>growthPerLevel</c> раз за передел», с якорями по корневым
/// материалам. Тот инструмент решал реальную задачу своего времени (осесть на сырье было выгоднее,
/// чем перерабатывать), но после перехода на cost-plus его выход перестал влиять на цену вообще, и
/// он тихо разошёлся с движком: держал собственную копию наценки со значением <c>1.05</c> против
/// <c>1.30</c> в <c>MarketSaleCalculator</c>. Дублированная константа удалена вместе со старой
/// формулой — наценка теперь приходит аргументом, дублировать нечего.</para>
///
/// <para>Чистая функция: себестоимость приходит готовым словарём (её считает
/// <c>Game.Engine.MaterialCostCalculator</c>, а <c>Game.Config</c> не может ссылаться на
/// <c>Game.Engine</c> — направление зависимостей обратное), конфиг не мутируется, запись —
/// отдельным <see cref="Apply"/>, как и у <see cref="DifficultyScaler"/>.</para>
/// </summary>
public static class SystemSalePriceLadderCalculator
{
    /// <summary>Одна строка лестницы — материал, его себестоимость и посчитанная от неё цена сбыта; было/станет для предпросмотра до применения.</summary>
    public sealed record MaterialLadderRow(
        string MaterialId,
        string MaterialName,
        string SectorId,
        int Level,
        decimal UnitCost,
        decimal OldPrice,
        decimal NewPrice)
    {
        /// <summary>Наценка над себестоимостью, долей (0.30 = +30%). У бесплатного материала — 0.</summary>
        public decimal MarginRate => UnitCost > 0m ? NewPrice / UnitCost - 1m : 0m;

        /// <summary>
        /// Прибыль с единицы при продаже системе на нетронутом рынке — <c>цена − себестоимость</c>.
        /// Под экзогенной ценой это и есть настоящая прибыль уровня (при cost-plus она была
        /// <c>0.30 × собственный передел</c> и от глубины не зависела вовсе).
        /// </summary>
        public decimal ProfitPerUnit => NewPrice - UnitCost;
    }

    /// <summary>
    /// Считает лестницу по всем материалам, у которых есть запись в
    /// <see cref="EconomyConfig.BaseMarketPerMaterial"/>. Порядок строк канонический (сектор →
    /// уровень → код материала), не порядок словаря — AGENTS §2, правило 6.
    /// </summary>
    /// <param name="config">Конфиг, чьи цены пересчитываются.</param>
    /// <param name="unitCostByMaterialId">Себестоимость единицы каждого материала (<c>Game.Engine.MaterialCostCalculator.CalculateAll</c>).</param>
    /// <param name="baseMargin">Базовая наценка над себестоимостью, долей — общая для всех уровней (0.30 = +30%).</param>
    /// <param name="depthBonusPerLevel">Прибавка к наценке за каждый уровень передела, долей; 0 воспроизводит прежнее правило cost-plus.</param>
    public static IReadOnlyList<MaterialLadderRow> Calculate(
        ResolvedGameConfig config,
        IReadOnlyDictionary<string, decimal> unitCostByMaterialId,
        decimal baseMargin,
        decimal depthBonusPerLevel)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(unitCostByMaterialId);
        if (baseMargin < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseMargin), baseMargin, "Base margin must not be negative: the system may not buy below cost.");
        }
        if (depthBonusPerLevel < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depthBonusPerLevel), depthBonusPerLevel,
                "Depth bonus must not be negative: deeper processing may not be rewarded less than shallower.");
        }

        var marketByMaterialId = config.Raw.Economy.BaseMarketPerMaterial.ToDictionary(m => m.MaterialId);
        var rows = new List<MaterialLadderRow>();

        foreach (var material in config.Materials.Values
                     .Where(m => marketByMaterialId.ContainsKey(m.Id))
                     .OrderBy(m => m.Sector.Id, StringComparer.Ordinal)
                     .ThenBy(m => m.Level)
                     .ThenBy(m => m.Id, StringComparer.Ordinal))
        {
            if (!unitCostByMaterialId.TryGetValue(material.Id, out var unitCost))
            {
                throw new InvalidOperationException(
                    $"No unit cost supplied for material '{material.Id}': the price ladder is computed from cost, " +
                    "so every material with a market entry must have one (see MaterialCostCalculator.CalculateAll).");
            }

            var margin = baseMargin + depthBonusPerLevel * material.Level;
            rows.Add(new MaterialLadderRow(
                material.Id,
                material.Name,
                material.Sector.Id,
                material.Level,
                unitCost,
                marketByMaterialId[material.Id].BaseSellPrice,
                unitCost * (1m + margin)));
        }

        return rows;
    }

    /// <summary>
    /// Применяет посчитанную лестницу к конфигу: возвращает новый <see cref="GameConfig"/> с
    /// обновлённым <see cref="EconomyConfig.BaseMarketPerMaterial"/>. Ёмкость не трогает — это
    /// отдельный, не связанный с ценой параметр. Не валидирует и не пересобирает
    /// <see cref="ResolvedGameConfig"/> — обязанность вызывающего кода, симметрично
    /// <see cref="DifficultyScaler.Apply"/>.
    /// </summary>
    public static GameConfig Apply(GameConfig config, IReadOnlyList<MaterialLadderRow> rows)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(rows);

        var newPriceByMaterialId = rows.ToDictionary(r => r.MaterialId, r => r.NewPrice);
        var newMarket = config.Economy.BaseMarketPerMaterial
            .Select(m => newPriceByMaterialId.TryGetValue(m.MaterialId, out var newPrice) ? m with { BaseSellPrice = newPrice } : m)
            .ToList();

        return config with { Economy = config.Economy with { BaseMarketPerMaterial = newMarket } };
    }
}
