using Game.Config.Loading;

namespace Game.Balancing.Tests;

/// <summary>
/// <see cref="TeamSteadyStateCalculator"/> — направление A плана исследований, продолжение
/// (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>, 2026-08-23): окупаемость уровня
/// (<see cref="ProductionCostLevelCalculatorTests"/>) сознательно не считает зарплату/поколение/R&amp;D,
/// эта проверка — дополнение, не замена.
/// </summary>
public class TeamSteadyStateCalculatorTests
{
    [Fact]
    public void NetPerTurn_Subtracts_Generation_And_Rnd_Ceilings_From_Margin_That_Already_Nets_Salary()
    {
        // 1 фабрика, 1 рабочий: собственный передел = FixedCostPerTurn 30 + электричество 0 +
        // зарплата 1×5 = 35, прибыль = 35×0.3 = 10.5 — это уже ЧИСТАЯ маржа сверх операционных
        // расходов, зарплата внутри неё (docs/economy-accounting-audit.md, дефект 3), поэтому
        // второй раз она не вычитается. Поколение=300 (потолок, один на сектор),
        // R&D=1×200=200 (потолок на фабрику). Net = 10.5 - 300 - 200 = -489.5.
        var config = ProductionCostLevelCalculatorTests.BuildSingleFactoryConfig(buildCost: 1000m, fixedCostPerTurn: 30m, productionRate: 100m);
        var rows = ProductionCostLevelCalculator.Calculate(config, workersPerFactory: 1);

        var states = TeamSteadyStateCalculator.Calculate(rows, config);
        var state = states.Single();

        Assert.Equal("A", state.SectorId);
        Assert.Equal(10.5m, state.ProfitPerTurn);
        Assert.Equal(5m, state.SalaryPerTurn);
        Assert.Equal(300m, state.GenerationResearchPerTurn);
        Assert.Equal(200m, state.RndPerTurn);
        Assert.Equal(-489.5m, state.NetPerTurn);
    }

    /// <summary>
    /// Поколение — потолок ОДИН на сектор/команду, R&amp;D — потолок на КАЖДУЮ фабрику отдельно (в
    /// реальном движке <c>SetRndCommitment</c> вызывается по фабрике, <c>SetGenerationResearchCommitment</c>
    /// — по команде целиком, см. <c>SimpleBot.UpdateInvestmentPace</c>) — это разное масштабирование,
    /// таблица с двумя фабриками должна показать именно эту асимметрию, не одинаковый рост обоих.
    /// </summary>
    [Fact]
    public void GenerationResearch_Is_One_Ceiling_Per_Sector_While_Rnd_Scales_Per_Factory()
    {
        var config = ProductionCostLevelCalculatorTests.BuildThreeLevelChainConfig(
            buildCosts: [500m, 1200m, 2500m], fixedCosts: [6m, 14.4m, 30m], productionRates: [50m, 20m, 8m]);
        var rows = ProductionCostLevelCalculator.Calculate(config, workersPerFactory: 1);

        var state = TeamSteadyStateCalculator.Calculate(rows, config).Single();

        Assert.Equal(300m, state.GenerationResearchPerTurn); // не 300×3
        Assert.Equal(3 * 200m, state.RndPerTurn); // 3 фабрики × потолок 200 каждая
    }
}
