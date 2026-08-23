namespace Game.Balancing;

/// <summary>
/// Направление C плана исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>,
/// 2026-08-24): «BuildCost(уровень) и ProductionRate(уровень) — параметризованные функции
/// (геометрический рост/спад), не таблица чисел» — прогоняет направление A (<see
/// cref="ProductionCostLevelCalculator.FactoryRecipeCost.PaybackTurns"/>) по сетке (коэффициент роста
/// BuildCost/FixedCostPerTurn × коэффициент спада ProductionRate), не по одной конкретной цепочке из
/// файла. Результат — не одно число «сработало/не сработало» для ЭТОЙ конкретной content-цепочки, а
/// область на карте, где геометрическая цепочка ЛЮБОЙ такой формы в принципе окупается на каждом
/// уровне при заданной глубине — отвечает на вопрос «выбор конкретных чисел в конфиге был плохим, или
/// сама идея такой формы цепочки в принципе не может быть сбалансирована».
/// <para>
/// Уровень 0: <c>BuildCost</c>/<c>FixedCostPerTurn</c> = базовые значения, <c>ProductionRate</c> =
/// базовое значение. Уровень i: <c>BuildCost</c>/<c>FixedCostPerTurn</c> умножены на
/// <c>growth^i</c> (растут вместе — реалистичное допущение: чем крупнее стройка, тем дороже
/// содержание), <c>ProductionRate</c> умножен на <c>decay^i</c> (падает — типично для глубоких
/// переделов, у которых на выходе меньше физических единиц, чем на входе). Число единиц входа на
/// единицу выхода (<c>inputQuantityPerLevel</c>) фиксировано по всей сетке — не третья ось сетки,
/// иначе результат не читается глазами (двумерная карта, не куб).
/// </para>
/// </summary>
public static class GeometricChainSweep
{
    /// <summary>Один узел сетки — метрика направления A для цепочки, собранной с этими <paramref name="BuildCostGrowth"/>/<paramref name="ProductionRateDecay"/>.</summary>
    public sealed record CellResult
    {
        public required decimal BuildCostGrowth { get; init; }
        public required decimal ProductionRateDecay { get; init; }

        /// <summary>Все уровни окупаются не дольше порога — вся геометрическая форма цепочки жизнеспособна при этой глубине.</summary>
        public required bool AllLevelsViable { get; init; }

        /// <summary>Первый (самый мелкий) уровень, который не укладывается в порог окупаемости — <c>null</c>, если все уложились.</summary>
        public int? FirstFailingLevel { get; init; }

        /// <summary>Самая долгая окупаемость среди всех уровней (<c>null</c>, если хотя бы один уровень не окупается никогда).</summary>
        public decimal? WorstPaybackTurns { get; init; }
    }

    /// <summary>
    /// Прогоняет сетку <paramref name="buildCostGrowthSteps"/> × <paramref name="productionRateDecaySteps"/>
    /// — для каждой пары собирает <paramref name="levels"/>-уровневую синтетическую цепочку (<see
    /// cref="SyntheticChainConfigBuilder"/>) и проверяет направление A (окупаемость каждого уровня не
    /// дольше <paramref name="paybackWarningTurns"/> при продаже 100% системе).
    /// </summary>
    public static IReadOnlyList<CellResult> Run(
        decimal baseBuildCost,
        decimal baseFixedCostPerTurn,
        decimal baseProductionRate,
        int levels,
        decimal inputQuantityPerLevel,
        IReadOnlyList<decimal> buildCostGrowthSteps,
        IReadOnlyList<decimal> productionRateDecaySteps,
        decimal paybackWarningTurns,
        int workersPerFactory)
    {
        ArgumentNullException.ThrowIfNull(buildCostGrowthSteps);
        ArgumentNullException.ThrowIfNull(productionRateDecaySteps);
        if (levels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(levels), levels, "Нужен хотя бы один уровень.");
        }

        var results = new List<CellResult>();
        foreach (var growth in buildCostGrowthSteps)
        {
            foreach (var decay in productionRateDecaySteps)
            {
                var levelParams = Enumerable.Range(0, levels)
                    .Select(i => new SyntheticChainConfigBuilder.LevelParams(
                        BuildCost: baseBuildCost * Pow(growth, i),
                        FixedCostPerTurn: baseFixedCostPerTurn * Pow(growth, i),
                        ProductionRate: baseProductionRate * Pow(decay, i)))
                    .ToList();

                var config = SyntheticChainConfigBuilder.Build(levelParams, inputQuantityPerLevel);
                var rows = ProductionCostLevelCalculator.Calculate(config, workersPerFactory);

                int? firstFailingLevel = null;
                decimal? worstPayback = null;
                var anyNeverPaysBack = false;
                foreach (var row in rows.OrderBy(r => r.Level))
                {
                    if (row.PaybackTurns is not { } payback)
                    {
                        anyNeverPaysBack = true;
                        firstFailingLevel ??= row.Level;
                        continue;
                    }

                    if (payback > paybackWarningTurns)
                    {
                        firstFailingLevel ??= row.Level;
                    }

                    if (worstPayback is null || payback > worstPayback)
                    {
                        worstPayback = payback;
                    }
                }

                results.Add(new CellResult
                {
                    BuildCostGrowth = growth,
                    ProductionRateDecay = decay,
                    AllLevelsViable = firstFailingLevel is null,
                    FirstFailingLevel = firstFailingLevel,
                    WorstPaybackTurns = anyNeverPaysBack ? null : worstPayback,
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Целочисленная степень через <c>decimal</c> умножением в цикле, не <c>Math.Pow</c> (тот работает
    /// с <c>double</c> — на глубине 10-15 уровней и коэффициентах роста в разы накопленная погрешность
    /// double уже заметна на глаз в отчёте; сетка явно параметризована целыми степенями уровня, не
    /// произвольным показателем, поэтому цикл умножения и быстрее, и точнее).
    /// </summary>
    private static decimal Pow(decimal value, int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= value;
        }

        return result;
    }
}
