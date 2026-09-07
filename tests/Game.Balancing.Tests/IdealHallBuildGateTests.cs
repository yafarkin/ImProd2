using Game.Engine;

namespace Game.Balancing.Tests;

/// <summary>
/// Гейт «успеет ли отбить хотя бы наём» в <see cref="IdealHallCalculator"/> (docs/TODO.md №29,
/// 2026-09-07): зал не строит пару (тип, рецепт), которая за оставшиеся до конца партии ходы не
/// отобьёт даже свой разовый наём. Порог узкий намеренно — полный <c>BuildCost</c> возвращается
/// остаточной стоимостью фабрики, поэтому гейт «по всему BuildCost» сам ломал бы верхнюю границу
/// (см. doc-comment <c>IdealHallCalculator.BuildNewlyUnlockedFactories</c>).
/// </summary>
public class IdealHallBuildGateTests
{
    // SyntheticChainConfigBuilder зашивает HireCostPerWorker=50, BaseWorkerCount=10 → наём 500 ¤ на
    // фабрику; SalaryPerWorkerPerTurn=5 → зарплата 50 ¤/ход. Прибыль/ход = 0.30 × (FixedCostPerTurn +
    // зарплата). Для самого мелкого уровня (FCP≈6.67) это ≈17 ¤/ход — за 1 ход наём (500) не отбить,
    // за 90 — с запасом.
    private static readonly SyntheticChainConfigBuilder.LevelParams[] Chain =
    {
        new(BuildCost: 350m, FixedCostPerTurn: 6.67m, ProductionRate: 50m),
        new(BuildCost: 800m, FixedCostPerTurn: 20m, ProductionRate: 20m),
        new(BuildCost: 1650m, FixedCostPerTurn: 46.67m, ProductionRate: 7.5m),
    };

    [Fact]
    public void One_Turn_Left_Builds_Nothing_Because_Even_Hiring_Would_Not_Pay_Back()
    {
        var config = SyntheticChainConfigBuilder.Build(Chain, inputQuantityPerLevel: 2m);

        var result = IdealHallCalculator.Calculate(config, maxTurns: 1);

        var branch = Assert.Single(result.Branches);
        Assert.False(
            branch.ExpensesByType.ContainsKey(FinanceHistoryCalculator.OperationType.FactoryBuilt),
            "зал построил фабрику, которой негде окупить даже наём за один оставшийся ход");
        Assert.False(branch.ExpensesByType.ContainsKey(FinanceHistoryCalculator.OperationType.WorkersHired));
        Assert.Equal(0m, branch.ValueByTurn[^1]);
    }

    [Fact]
    public void A_Full_Length_Game_Still_Builds_The_Whole_Chain()
    {
        var config = SyntheticChainConfigBuilder.Build(Chain, inputQuantityPerLevel: 2m);

        var result = IdealHallCalculator.Calculate(config, maxTurns: 90);

        var branch = Assert.Single(result.Branches);
        Assert.True(branch.ExpensesByType[FinanceHistoryCalculator.OperationType.FactoryBuilt] > 0m);
        Assert.True(branch.ValueByTurn[^1] > 0m, "полноразмерная партия должна закончиться в плюсе");
    }
}
