using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Итоговый счёт по ликвидационной стоимости (Блок 7.2, SPEC §5.11).</summary>
public class FinalScoreCalculatorTests
{
    // TestGameConfig: WarehouseLiquidationRate = 0.5; iron-mine/steel-mill: BuildCost = 100, LiquidationValueCoefficient = 0.5.
    private static readonly Config.Economy.EconomyConfig Economy = TestGameConfig.Resolved.Raw.Economy;
    private static readonly IReadOnlyList<Config.Catalog.FactoryDefinitionConfig> FactoryDefinitions = TestGameConfig.Resolved.Raw.FactoryDefinitions;

    private static Market NewMarket()
    {
        var market = new Market();
        market.ReplaceQuotes(
            new Dictionary<string, MaterialQuote>
            {
                [TestGameConfig.Ore.Id] = new(price: 10m, capacity: 100m),
                [TestGameConfig.Sheet.Id] = new(price: 25m, capacity: 8m),
            },
            electricityPrice: 1m);

        return market;
    }

    [Fact]
    public void A_Team_With_No_Warehouse_Or_Factories_Scores_Cash_Minus_Debt()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        team.TakeLoan(1000m); // Balance += 1000, Debt += 1000
        team.Credit(500m); // Balance = 1500

        var result = FinalScoreCalculator.Calculate(team, NewMarket(), Economy, FactoryDefinitions);

        Assert.Equal(1500m, result.Cash);
        Assert.Equal(1000m, result.Debt);
        Assert.Equal(0m, result.WarehouseValue);
        Assert.Equal(0m, result.FactoriesValue);
        Assert.Equal(500m, result.Score); // 1500 - 1000 + 0 + 0
    }

    [Fact]
    public void Warehouse_Stock_Is_Valued_At_A_Fraction_Of_The_Current_Market_Price()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        team.Warehouse.Add(TestGameConfig.Ore, 10m, 0m); // 10 * 10 * 0.5 = 50
        team.Warehouse.Add(TestGameConfig.Sheet, 4m, 0m); // 4 * 25 * 0.5 = 50

        var result = FinalScoreCalculator.Calculate(team, NewMarket(), Economy, FactoryDefinitions);

        Assert.Equal(100m, result.WarehouseValue);
        Assert.Equal(100m, result.Score); // Cash=0, Debt=0
    }

    [Fact]
    public void Factories_Are_Valued_At_A_Fraction_Of_Their_Build_Cost_Regardless_Of_Rnd_Investment()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        mine.InvestInRnd(1000m); // не должно повлиять на итоговый счёт (SPEC §5.11)

        var result = FinalScoreCalculator.Calculate(team, NewMarket(), Economy, FactoryDefinitions);

        Assert.Equal(50m, result.FactoriesValue); // 100 (BuildCost) * 0.5 (LiquidationValueCoefficient)
        Assert.Equal(50m, result.Score);
    }

    [Fact]
    public void Score_Combines_Cash_Debt_Warehouse_And_Factories()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        team.TakeLoan(1000m);
        team.Warehouse.Add(TestGameConfig.Ore, 10m, 0m); // 50
        team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine); // 50
        team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill); // 50

        var result = FinalScoreCalculator.Calculate(team, NewMarket(), Economy, FactoryDefinitions);

        Assert.Equal(1000m, result.Cash);
        Assert.Equal(1000m, result.Debt);
        Assert.Equal(50m, result.WarehouseValue);
        Assert.Equal(100m, result.FactoriesValue);
        Assert.Equal(150m, result.Score); // 1000 - 1000 + 50 + 100
    }
}
