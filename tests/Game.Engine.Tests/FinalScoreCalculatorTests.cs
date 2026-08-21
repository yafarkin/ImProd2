using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Итоговый счёт по ликвидационной стоимости (Блок 7.2, SPEC §5.11).</summary>
public class FinalScoreCalculatorTests
{
    // TestGameConfig: WarehouseLiquidationRate = 0.5; iron-mine/steel-mill: BuildCost = 100, LiquidationValueCoefficient = 0.5.
    private static readonly Config.Economy.EconomyConfig Economy = TestGameConfig.Resolved.Raw.Economy;
    private static readonly IReadOnlyList<Config.Catalog.FactoryDefinitionConfig> FactoryDefinitions = TestGameConfig.Resolved.Raw.FactoryDefinitions;

    private static readonly IReadOnlyDictionary<string, decimal> MaterialCosts = new Dictionary<string, decimal>
    {
        [TestGameConfig.Ore.Id] = 10m,
        [TestGameConfig.Sheet.Id] = 25m,
    };

    [Fact]
    public void A_Team_With_No_Warehouse_Or_Factories_Scores_Cash()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        team.Credit(1500m);

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, Economy, FactoryDefinitions);

        Assert.Equal(1500m, result.Cash);
        Assert.Equal(0m, result.WarehouseValue);
        Assert.Equal(0m, result.FactoriesValue);
        Assert.Equal(1500m, result.Score); // 1500 + 0 + 0
    }

    [Fact]
    public void Warehouse_Stock_Is_Valued_At_A_Fraction_Of_The_Material_Cost()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        team.Warehouse.Add(TestGameConfig.Ore, 10m, 0m); // 10 * 10 * 0.5 = 50
        team.Warehouse.Add(TestGameConfig.Sheet, 4m, 0m); // 4 * 25 * 0.5 = 50

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, Economy, FactoryDefinitions);

        Assert.Equal(100m, result.WarehouseValue);
        Assert.Equal(100m, result.Score); // Cash=0
    }

    [Fact]
    public void Factories_Are_Valued_At_A_Fraction_Of_Their_Build_Cost_Regardless_Of_Rnd_Investment()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        mine.InvestInRnd(1000m); // не должно повлиять на итоговый счёт (SPEC §5.11)

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, Economy, FactoryDefinitions);

        Assert.Equal(50m, result.FactoriesValue); // 100 (BuildCost) * 0.5 (LiquidationValueCoefficient)
        Assert.Equal(50m, result.Score);
    }

    [Fact]
    public void Score_Combines_Cash_Warehouse_And_Factories()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        team.Credit(1000m);
        team.Warehouse.Add(TestGameConfig.Ore, 10m, 0m); // 50
        team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine); // 50
        team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill); // 50

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, Economy, FactoryDefinitions);

        Assert.Equal(1000m, result.Cash);
        Assert.Equal(50m, result.WarehouseValue);
        Assert.Equal(100m, result.FactoriesValue);
        Assert.Equal(1150m, result.Score); // 1000 + 50 + 100
    }
}
