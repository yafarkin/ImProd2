using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing.Tests;

/// <summary>
/// Проверяет утверждение из <c>docs/levers.md</c> про рычаг «численность рабочих»: под ценообразованием
/// «себестоимость + фиксированная наценка» перенайм сверх базовой численности **не всегда выгоден**, и
/// знак эффекта определяется структурой расходов фабрики, а не общим правилом.
///
/// <para>
/// Причина в том, что цена продажи считается <see cref="MaterialCostCalculator"/> по эталонной фабрике
/// первого уровня с <c>BaseWorkerCount</c> рабочих — то есть от численности КОНКРЕТНОЙ фабрики она не
/// зависит вовсе. Значит лишний рабочий даёт выручку через выпуск (а тот растёт с убывающей отдачей,
/// <see cref="ProductionCalculator"/>: сверх базовой численности каждый рабочий считается за
/// <c>DiminishingReturnsFactor</c>), но зарплату тянет линейно.
/// </para>
///
/// <para>
/// Считаем не по формуле из документа, а через сами калькуляторы движка — иначе тест доказывал бы
/// только то, что две копии одной формулы совпадают (ровно та ошибка, что стоила проекту недель, см.
/// <c>docs/economy-accounting-audit.md</c>).
/// </para>
/// </summary>
public class OverStaffingEconomicsTests
{
    private static ResolvedGameConfig LoadTrainingChain() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "training-1-sector.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    /// <summary>
    /// Прибыль фабрики за ход при заданной численности: выручка от продажи всего выпуска системе
    /// минус собственные расходы (сырьё по его себестоимости, содержание, электричество, зарплата).
    /// Ёмкость рынка намеренно не ограничиваем — здесь проверяется рычаг найма, не переполнение сбыта.
    /// </summary>
    private static decimal ProfitPerTurn(ResolvedGameConfig config, string factoryDefinitionId, int workers)
    {
        var definition = config.FactoryDefinitions.First(d => d.Id == factoryDefinitionId);
        var recipe = definition.Recipes[0];
        var costs = MaterialCostCalculator.CalculateAll(config);

        var factory = new Factory(Ulid.NewUlid(), definition.Sector, definition, recipe);
        factory.Hire(workers);
        var output = ProductionCalculator
            .CalculateCapacityBreakdown(factory, config.Raw.WorkerProductivity, config.Raw.Rnd)
            .TheoreticalMaxOutput;

        var revenue = output * costs[recipe.Output.Id] * MarketSaleCalculator.SystemSaleMarginMultiplier;

        var batches = recipe.OutputQuantity > 0 ? output / recipe.OutputQuantity : 0m;
        var inputCost = recipe.Inputs.Sum(input => input.Quantity * batches * costs[input.Material.Id]);
        var electricity = output
                          * config.Raw.Economy.ElectricityConsumptionPerOutputUnit
                          * config.Raw.Economy.ElectricityBasePrice;
        var salary = FinanceCalculator.CalculateSalaries(workers, config.Raw.WorkerProductivity);
        var upkeep = config.Raw.FactoryDefinitions.First(d => d.Id == definition.Id).FixedCostPerTurn;

        return revenue - inputCost - electricity - salary - upkeep;
    }

    /// <summary>
    /// Сырьевая фабрика с маленьким содержанием и без входов: перенайм режет прибыль. Зарплата растёт
    /// линейно, а компенсировать её нечем — содержание крошечное, входов нет, выпуск растёт вдвое
    /// медленнее численности.
    /// </summary>
    [Fact]
    public void Over_Staffing_A_Cheap_Raw_Factory_Reduces_Its_Profit()
    {
        var config = LoadTrainingChain();
        var baseWorkers = config.Raw.WorkerProductivity.BaseWorkerCount;

        var atBase = ProfitPerTurn(config, "scrap-yard", baseWorkers);
        var doubled = ProfitPerTurn(config, "scrap-yard", baseWorkers * 2);

        Assert.True(
            doubled < atBase,
            $"Ожидали, что перенайм на дешёвой сырьевой фабрике невыгоден, но прибыль выросла: " +
            $"{atBase:F2} → {doubled:F2} при {baseWorkers} → {baseWorkers * 2} рабочих.");
    }

    /// <summary>
    /// Глубокий передел с большим содержанием и дорогими входами: там перенайм, наоборот, выгоден —
    /// постоянные расходы размазываются по большему выпуску быстрее, чем набегает зарплата.
    /// </summary>
    [Fact]
    public void Over_Staffing_A_Deep_Expensive_Factory_Increases_Its_Profit()
    {
        var config = LoadTrainingChain();
        var baseWorkers = config.Raw.WorkerProductivity.BaseWorkerCount;

        var atBase = ProfitPerTurn(config, "fabrication-shop", baseWorkers);
        var doubled = ProfitPerTurn(config, "fabrication-shop", baseWorkers * 2);

        Assert.True(
            doubled > atBase,
            $"Ожидали, что перенайм на глубоком переделе выгоден, но прибыль упала: " +
            $"{atBase:F2} → {doubled:F2} при {baseWorkers} → {baseWorkers * 2} рабочих.");
    }

    /// <summary>
    /// Само правило, а не два его частных случая: знак эффекта совпадает со знаком
    /// <c>0.15·сырьё + 0.65·содержание + 0.15·электричество − 0.35·зарплата</c> (вывод — в
    /// <c>docs/levers.md</c>, раздел про численность). Если формулу в документе когда-нибудь
    /// перепишут неверно, этот тест покраснеет на реальной цепочке.
    /// </summary>
    [Fact]
    public void The_Sign_Of_The_Over_Staffing_Effect_Matches_The_Documented_Rule()
    {
        var config = LoadTrainingChain();
        var costs = MaterialCostCalculator.CalculateAll(config);
        var baseWorkers = config.Raw.WorkerProductivity.BaseWorkerCount;
        var salaryAtBase = FinanceCalculator.CalculateSalaries(baseWorkers, config.Raw.WorkerProductivity);
        var margin = MarketSaleCalculator.SystemSaleMarginMultiplier;
        var returns = config.Raw.WorkerProductivity.DiminishingReturnsFactor;

        foreach (var definition in config.FactoryDefinitions)
        {
            var recipe = definition.Recipes[0];
            var factory = new Factory(Ulid.NewUlid(), definition.Sector, definition, recipe);
            factory.Hire(baseWorkers);
            var output = ProductionCalculator
                .CalculateCapacityBreakdown(factory, config.Raw.WorkerProductivity, config.Raw.Rnd)
                .TheoreticalMaxOutput;

            var batches = recipe.OutputQuantity > 0 ? output / recipe.OutputQuantity : 0m;
            var inputCost = recipe.Inputs.Sum(input => input.Quantity * batches * costs[input.Material.Id]);
            var electricity = output
                              * config.Raw.Economy.ElectricityConsumptionPerOutputUnit
                              * config.Raw.Economy.ElectricityBasePrice;

            var predicted = (margin * returns - returns) * (inputCost + electricity)
                            + margin * returns * config.Raw.FactoryDefinitions.First(d => d.Id == definition.Id).FixedCostPerTurn
                            + (margin * returns - 1m) * salaryAtBase;

            var actual = ProfitPerTurn(config, definition.Id, baseWorkers * 2) - ProfitPerTurn(config, definition.Id, baseWorkers);

            Assert.True(
                Math.Sign(predicted) == Math.Sign(actual),
                $"{definition.Id}: правило предсказало {(predicted > 0 ? "выгоду" : "убыток")} от перенайма " +
                $"({predicted:F2}), а на деле прибыль изменилась на {actual:F2}.");
        }
    }
}
