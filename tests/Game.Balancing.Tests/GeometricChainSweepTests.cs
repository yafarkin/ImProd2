namespace Game.Balancing.Tests;

/// <summary>
/// Направление C плана исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>,
/// 2026-08-24). Главная находка сессии, которую эти тесты фиксируют как регрессию: при
/// себестоимостном (cost-plus) ценообразовании (наценка накладывается на УЖЕ ПОСЧИТАННУЮ
/// себестоимость, не на независимую рыночную цену) окупаемость уровня — функция ТОЛЬКО отношения
/// <c>BuildCost/(FixedCostPerTurn+InputCost)</c>, не самих коэффициентов роста/спада по отдельности:
/// если <c>BuildCost</c> и <c>FixedCostPerTurn</c> растут по уровню С ОДНИМ И ТЕМ ЖЕ коэффициентом
/// (как в <see cref="GeometricChainSweep.Run"/> — оба параметризованы одним <c>growth</c>), это
/// отношение НЕ МЕНЯЕТСЯ ни от значения роста, ни от глубины, ни от спада ProductionRate. Значит
/// «докрутить рост/спад» в принципе не может ни сломать здоровую цепочку, ни починить нездоровую —
/// чинить нужно САМО базовое отношение (см. направление B, окупаемость уровня 0), не форму роста.
/// </summary>
public class GeometricChainSweepTests
{
    private static readonly IReadOnlyList<decimal> ThreeGrowthSteps = [1.0m, 2.0m, 4.0m];
    private static readonly IReadOnlyList<decimal> ThreeDecaySteps = [1.0m, 0.5m, 0.1m];

    [Fact]
    public void Healthy_Base_Ratio_Stays_Viable_At_Every_Growth_And_Decay_Combination()
    {
        // BuildCost/FixedCostPerTurn = 350/17 ≈ 20.6 => payback уровня 0 ≈ 350/(17×0.3) ≈ 68.6 < 75:
        // здоровое отношение, взятое из реального фикса направления B этой же ветки (debug-minimal.json).
        var results = GeometricChainSweep.Run(
            baseBuildCost: 350m, baseFixedCostPerTurn: 17m, baseProductionRate: 100m,
            levels: 6, inputQuantityPerLevel: 2m,
            buildCostGrowthSteps: ThreeGrowthSteps, productionRateDecaySteps: ThreeDecaySteps,
            paybackWarningTurns: 75m, workersPerFactory: 10);

        Assert.All(results, r => Assert.True(r.AllLevelsViable,
            $"growth={r.BuildCostGrowth}, decay={r.ProductionRateDecay}: первый провал на уровне {r.FirstFailingLevel}."));
    }

    [Fact]
    public void Unhealthy_Base_Ratio_Cannot_Be_Fixed_By_Growth_Or_Decay_Tuning()
    {
        // BuildCost/FixedCostPerTurn = 500/16.67 = 30 => payback уровня 0 = 500/(16.67×0.3) = 100 > 75:
        // то же нездоровое отношение, что было в debug-minimal.json ДО фикса направления B.
        var results = GeometricChainSweep.Run(
            baseBuildCost: 500m, baseFixedCostPerTurn: 16.67m, baseProductionRate: 100m,
            levels: 6, inputQuantityPerLevel: 2m,
            buildCostGrowthSteps: ThreeGrowthSteps, productionRateDecaySteps: ThreeDecaySteps,
            paybackWarningTurns: 75m, workersPerFactory: 10);

        Assert.All(results, r =>
        {
            Assert.False(r.AllLevelsViable);
            Assert.Equal(0, r.FirstFailingLevel);
        });
    }

    /// <summary>
    /// Прямая проверка самого механизма (не только его следствия на всей сетке): уровень 0 не имеет
    /// входов вовсе, поэтому его <c>PaybackTurns</c> буквально не может зависеть от <c>decay</c>
    /// (ProductionRate уровня 0 не влияет на прибыль/ход при cost-plus наценке — см. doc-comment
    /// класса), а от <c>growth</c> не зависит, потому что <c>BuildCost</c> и <c>FixedCostPerTurn</c>
    /// растут вместе с ним один в один.
    /// </summary>
    [Fact]
    public void Single_Level_Chain_Viability_Is_Invariant_To_Growth_And_Decay()
    {
        var results = GeometricChainSweep.Run(
            baseBuildCost: 350m, baseFixedCostPerTurn: 17m, baseProductionRate: 100m,
            levels: 1, inputQuantityPerLevel: 2m,
            buildCostGrowthSteps: ThreeGrowthSteps, productionRateDecaySteps: ThreeDecaySteps,
            paybackWarningTurns: 75m, workersPerFactory: 10);

        Assert.All(results, r => Assert.True(r.AllLevelsViable));
        var distinctPaybacks = results.Select(r => r.WorstPaybackTurns).Distinct().ToList();
        Assert.Single(distinctPaybacks);
    }
}
