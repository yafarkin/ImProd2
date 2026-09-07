using Game.Config.Economy;
using Game.Config.Loading;
using Game.Engine;

namespace Game.Balancing.Tests;

/// <summary>
/// Переезд калибровочной оснастки на общую базу выручки (блок 11.6,
/// <c>docs/external-economy.md</c> §9): прибыль уровня считается одной формулой
/// <c>выпуск × цена(выход) − Σ вход × цена(вход) − собственный передел</c> в обеих моделях
/// ценообразования.
/// </summary>
public class SystemSaleReferencePriceTests
{
    /// <summary>Четыре уровня: сырьё дешёвое и производительное, каждый следующий дороже и медленнее.</summary>
    private static ResolvedGameConfig CostPlusChain() => SyntheticChainConfigBuilder.Build(
    [
        new SyntheticChainConfigBuilder.LevelParams(BuildCost: 1000m, FixedCostPerTurn: 10m, ProductionRate: 800m),
        new SyntheticChainConfigBuilder.LevelParams(BuildCost: 1500m, FixedCostPerTurn: 15m, ProductionRate: 400m),
        new SyntheticChainConfigBuilder.LevelParams(BuildCost: 2250m, FixedCostPerTurn: 22m, ProductionRate: 200m),
        new SyntheticChainConfigBuilder.LevelParams(BuildCost: 3375m, FixedCostPerTurn: 33m, ProductionRate: 100m),
    ]);

    private static ResolvedGameConfig ExternalChain(decimal depthBonus)
    {
        var costPlus = CostPlusChain();
        var costs = MaterialCostCalculator.CalculateAll(costPlus);
        var ladder = SystemSalePriceLadderCalculator.Calculate(costPlus, costs, baseMargin: 0.30m, depthBonusPerLevel: depthBonus);
        var raw = SystemSalePriceLadderCalculator.Apply(costPlus.Raw, ladder);

        raw = raw with { Economy = raw.Economy with { PricingModel = PricingModel.External } };
        return GameConfigLoader.Load(GameConfigWriter.Save(raw));
    }

    /// <summary>
    /// Опорное свойство блока: при <see cref="PricingModel.CostPlus"/> общая формула тождественно
    /// сводится к прежней <c>0.30 × собственный передел</c>. Равенство точное, не приближённое —
    /// именно оно гарантирует, что переезд оснастки не сдвинул ни одного числа в уже
    /// откалиброванных цепочках.
    ///
    /// <para>Подстановка: <c>цена = себестоимость × 1.30</c>, себестоимость выхода =
    /// себестоимость входов + передел, цена входов = себестоимость входов × 1.30. Тогда
    /// <c>1.30·c − 1.30·(c − передел) − передел = 0.30 · передел</c>.</para>
    /// </summary>
    [Fact]
    public void Under_Cost_Plus_The_General_Formula_Reduces_Exactly_To_Thirty_Percent_Of_Conversion()
    {
        var rows = ProductionCostLevelCalculator.Calculate(CostPlusChain(), workersPerFactory: 10);

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(
            row.ConversionCost * (MarketSaleCalculator.SystemSaleMarginMultiplier - 1m),
            row.ProfitPerTurn,
            precision: 6));
    }

    /// <summary>
    /// Тождество денежной массы при cost-plus (<c>docs/economy-accounting-audit.md</c>): суммарная
    /// прибыль по конфигу равна <c>0.30 × Σ передела</c>. Регрессия на дефект 3 — начисление маржи
    /// на себестоимость входов по разу на каждом переделе давало завышение в 2.23×.
    /// </summary>
    [Fact]
    public void Under_Cost_Plus_Total_Profit_Still_Matches_The_Money_Mass_Identity()
    {
        var rows = ProductionCostLevelCalculator.Calculate(CostPlusChain(), workersPerFactory: 10);

        Assert.Equal(
            rows.Sum(r => r.ConversionCost) * 0.30m,
            rows.Sum(r => r.ProfitPerTurn),
            precision: 6);
    }

    /// <summary>
    /// Лестница с нулевой надбавкой за глубину не меняет экономику: под <c>External</c> с
    /// <c>DepthBonus = 0</c> прибыль каждого уровня совпадает с прибылью под <c>CostPlus</c>. Это и
    /// есть обещанная точка входа в перекалибровку — старт из заведомо проходимого состояния.
    /// </summary>
    [Fact]
    public void External_With_No_Depth_Bonus_Reproduces_Cost_Plus_Profit_Level_By_Level()
    {
        var costPlus = ProductionCostLevelCalculator.Calculate(CostPlusChain(), workersPerFactory: 10);
        var external = ProductionCostLevelCalculator.Calculate(ExternalChain(depthBonus: 0m), workersPerFactory: 10);

        Assert.Equal(costPlus.Count, external.Count);
        foreach (var (before, after) in costPlus.OrderBy(r => r.OutputMaterialId).Zip(external.OrderBy(r => r.OutputMaterialId)))
        {
            Assert.Equal(before.OutputMaterialId, after.OutputMaterialId);
            Assert.Equal(before.ProfitPerTurn, after.ProfitPerTurn, precision: 4);
        }
    }

    /// <summary>
    /// Содержательный результат всего перехода: с положительной надбавкой за глубину прибыль
    /// глубоких уровней растёт относительно cost-plus, а сырьевых — нет. Под cost-plus такое было
    /// невозможно по построению: прибыль там пропорциональна собственным издержкам уровня, а не
    /// ценности того, что он производит.
    /// </summary>
    [Fact]
    public void A_Depth_Bonus_Raises_Profit_At_Deep_Levels_And_Leaves_Raw_Material_Alone()
    {
        var flat = ProductionCostLevelCalculator.Calculate(ExternalChain(depthBonus: 0m), workersPerFactory: 10);
        var rewarded = ProductionCostLevelCalculator.Calculate(ExternalChain(depthBonus: 0.10m), workersPerFactory: 10);

        var flatByMaterial = flat.ToDictionary(r => r.OutputMaterialId);
        var deepest = rewarded.MaxBy(r => r.Level)!;
        var shallowest = rewarded.MinBy(r => r.Level)!;

        Assert.True(
            deepest.ProfitPerTurn > flatByMaterial[deepest.OutputMaterialId].ProfitPerTurn,
            "глубокий уровень обязан зарабатывать больше, чем при нулевой надбавке");
        Assert.Equal(
            flatByMaterial[shallowest.OutputMaterialId].ProfitPerTurn,
            shallowest.ProfitPerTurn,
            precision: 4);
    }

    /// <summary>Под <c>External</c> опорная цена берётся из конфига и от себестоимости не зависит.</summary>
    [Fact]
    public void Under_External_The_Reference_Price_Comes_From_Config_Not_From_Cost()
    {
        var config = ExternalChain(depthBonus: 0.10m);
        var prices = SystemSaleReferencePriceCalculator.CalculateAll(config, MaterialCostCalculator.CalculateAll(config));

        foreach (var market in config.Raw.Economy.BaseMarketPerMaterial)
        {
            Assert.Equal(market.BaseSellPrice, prices[market.MaterialId]);
        }
    }

    /// <summary>Под <c>CostPlus</c> опорная цена — это себестоимость с фиксированной наценкой.</summary>
    [Fact]
    public void Under_Cost_Plus_The_Reference_Price_Is_Cost_Times_The_Fixed_Margin()
    {
        var config = CostPlusChain();
        var costs = MaterialCostCalculator.CalculateAll(config);
        var prices = SystemSaleReferencePriceCalculator.CalculateAll(config, costs);

        foreach (var (materialId, cost) in costs)
        {
            Assert.Equal(cost * MarketSaleCalculator.SystemSaleMarginMultiplier, prices[materialId]);
        }
    }
}
