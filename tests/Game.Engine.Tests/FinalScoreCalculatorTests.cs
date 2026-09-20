using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>Итоговый счёт по остаточной стоимости (Блок 7.2, SPEC §5.11).</summary>
public class FinalScoreCalculatorTests
{
    // TestGameConfig: iron-mine/steel-mill: BuildCost = 100, LiquidationValueCoefficient = 0.5.
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

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, FactoryDefinitions);

        Assert.Equal(1500m, result.Cash);
        Assert.Equal(0m, result.WarehouseValue);
        Assert.Equal(0m, result.FactoriesValue);
        Assert.Equal(1500m, result.Score); // 1500 + 0 + 0
    }

    [Fact]
    public void Warehouse_Stock_Is_Valued_Exactly_At_Material_Cost()
    {
        // С 2026-08-23 (запрос пользователя) — ровно по себестоимости, без скидки на ликвидацию
        // (WarehouseLiquidationRate больше не участвует в этой формуле): сознательное упрощение,
        // одна и та же формула для бота и для реальной игры.
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        team.Warehouse.Add(TestGameConfig.Ore, 10m, 0m); // 10 * 10 = 100
        team.Warehouse.Add(TestGameConfig.Sheet, 4m, 0m); // 4 * 25 = 100

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, FactoryDefinitions);

        Assert.Equal(200m, result.WarehouseValue);
        Assert.Equal(200m, result.Score); // Cash=0
    }

    [Fact]
    public void A_Pristine_Factory_Is_Valued_At_Its_Full_Build_Cost_Regardless_Of_Rnd_Investment()
    {
        // С 2026-08-23 (запрос пользователя) остаточная стоимость привязана к Condition, не плоская
        // доля — у только что построенной фабрики Condition=1, значит стоит полную BuildCost, не
        // половину (раньше, при плоской доле, было бы 50 = 100 * 0.5).
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        mine.InvestInRnd(1000m); // не должно повлиять на итоговый счёт (SPEC §5.11)

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, FactoryDefinitions);

        Assert.Equal(100m, result.FactoriesValue); // 100 (BuildCost) * (0.5 + 0.5 * Condition=1) = 100
        Assert.Equal(100m, result.Score);
    }

    [Fact]
    public void A_Worn_Factory_Is_Valued_Between_The_Liquidation_Floor_And_The_Full_Build_Cost()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        mine.ApplyConditionChange(0.4m); // на полпути между полностью убитой (0) и новой (1)

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, FactoryDefinitions);

        // 100 * (0.5 + 0.5 * 0.4) = 100 * 0.7 = 70 — между полом 50 (Condition=0) и потолком 100 (Condition=1).
        Assert.Equal(70m, result.FactoriesValue);
        Assert.Equal(70m, result.Score);
    }

    [Fact]
    public void A_Fully_Depleted_Factory_Is_Valued_At_The_Liquidation_Floor()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        var mine = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        mine.ApplyConditionChange(0m);

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, FactoryDefinitions);

        Assert.Equal(50m, result.FactoriesValue); // 100 * (0.5 + 0.5 * 0) = 50 — тот же пол, что и раньше при плоской доле.
        Assert.Equal(50m, result.Score);
    }

    [Fact]
    public void Score_Combines_Cash_Warehouse_And_Factories()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", TestGameConfig.SectorA);
        team.Credit(1000m);
        team.Warehouse.Add(TestGameConfig.Ore, 10m, 0m); // 100
        team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine); // 100 (Condition=1 -> полная BuildCost)
        team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mill); // 100

        var result = FinalScoreCalculator.Calculate(team, MaterialCosts, FactoryDefinitions);

        Assert.Equal(1000m, result.Cash);
        Assert.Equal(100m, result.WarehouseValue);
        Assert.Equal(200m, result.FactoriesValue);
        Assert.Equal(1300m, result.Score); // 1000 + 100 + 200
    }
}
