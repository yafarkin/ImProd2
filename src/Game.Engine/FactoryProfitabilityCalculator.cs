using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Оценка прибыльности одной фабрики «если бы тик прошёл прямо сейчас» (запрос пользователя «можем
/// ли понять, фабрика вообще прибыльна или нет?») — чистая функция, ничего не мутирует и не
/// пишет в журнал, по тому же принципу, что и <see cref="ProductionCalculator"/>. Фабрика
/// оценивается как отдельный торгующий узел: покупает потреблённое сырьё и продаёт свой выход по
/// текущим рыночным ценам (<see cref="Market"/>) — сознательно другая метрика, чем себестоимость из
/// пирамиды сырья (<c>CostCalculator</c>/<c>DashboardDisplay.TryCalculateUnitCost</c>, используется
/// в другом месте страницы для другого вопроса — «сколько теоретически стоит сделать единицу с
/// нуля», а не «прибыльна ли эта фабрика по сегодняшним ценам»).
/// </summary>
public static class FactoryProfitabilityCalculator
{
    /// <summary>
    /// Оценка прибыльности одной фабрики за предстоящий тик при текущих остатках склада и рыночных
    /// ценах. <see cref="OverheadCost"/> — та же капитальная + переменная часть затрат на работу
    /// фабрики, что реально списывается за тик (<see cref="FactoryUpkeepPaid"/> +
    /// <see cref="FactoryProduced.OverheadCost"/>), 0 у вызывающей стороны, которая её не передала.
    /// </summary>
    public sealed record FactoryProfitabilityEstimate(
        decimal ProjectedOutputQuantity,
        decimal CapacityLimitedOutputQuantity,
        decimal Revenue,
        decimal InputCost,
        decimal WageCost,
        decimal OverheadCost,
        decimal Profit,
        bool HasPriceSignal);

    /// <summary>
    /// Считает <paramref name="factory"/> в группе с остальными фабриками команды того же уровня
    /// выхода (<paramref name="teamFactories"/> — обычно все фабрики команды, лишние уровни
    /// игнорируются) — та же группировка «кто конкурирует за один склад в один тик», что и в
    /// <see cref="GameSession.RunTick"/>, чтобы оценка учитывала реальную конкуренцию за дефицитное
    /// сырьё (<see cref="Factory.AllocationShare"/>), а не считала фабрику в отрыве от соседей.
    /// Возвращает <c>false</c>, если у выхода фабрики или у любого потреблённого ею материала ещё
    /// нет рыночной котировки (<see cref="Market.HasQuote"/>) — тот же приём отказа, что у
    /// <c>DashboardDisplay.TryCalculateUnitCost</c>. <paramref name="fixedCostPerTurn"/> и
    /// <paramref name="electricityConsumptionPerOutputUnit"/> по умолчанию 0 — тогда виджет считает
    /// как раньше, без капитальных затрат (это не заглушка «выключено по умолчанию», а обязанность
    /// вызывающей стороны передать значения из конфига, см. <see cref="FactoryHistoryCalculator"/>).
    /// </summary>
    public static bool TryCalculate(
        Factory factory, IReadOnlyList<Factory> teamFactories, Warehouse warehouse, Market market,
        WorkerProductivityConfig productivity, RndConfig rnd, decimal salaryPerWorkerPerTurn,
        out FactoryProfitabilityEstimate estimate,
        decimal fixedCostPerTurn = 0m,
        decimal electricityConsumptionPerOutputUnit = 0m)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(teamFactories);
        ArgumentNullException.ThrowIfNull(warehouse);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(productivity);
        ArgumentNullException.ThrowIfNull(rnd);

        var levelMates = teamFactories
            .Where(f => f.SelectedRecipe.Output.Level == factory.SelectedRecipe.Output.Level)
            .OrderBy(f => f.Id)
            .ToList();
        var results = ProductionCalculator.CalculateGroup(levelMates, warehouse, productivity, rnd);
        var result = results.Single(r => r.FactoryId == factory.Id);

        var outputMaterial = factory.SelectedRecipe.Output;
        if (!market.HasQuote(outputMaterial.Id))
        {
            estimate = new FactoryProfitabilityEstimate(
                result.OutputQuantity, result.CapacityLimitedOutputQuantity, 0m, 0m, 0m, 0m, 0m, HasPriceSignal: false);
            return false;
        }

        var inputCost = 0m;
        foreach (var (materialId, quantity) in result.ConsumedInputs)
        {
            if (!market.HasQuote(materialId))
            {
                estimate = new FactoryProfitabilityEstimate(
                    result.OutputQuantity, result.CapacityLimitedOutputQuantity, 0m, 0m, 0m, 0m, 0m, HasPriceSignal: false);
                return false;
            }

            inputCost += quantity * market.QuoteOf(materialId).Price;
        }

        var revenue = result.OutputQuantity * market.QuoteOf(outputMaterial.Id).Price;
        var wageCost = factory.Workers * salaryPerWorkerPerTurn;
        // Те же два слагаемых, что реально списываются с баланса при настоящем производстве (см.
        // FactoryUpkeepPaid и FactoryProduced.OverheadCost) — иначе оценка тут была бы систематически
        // оптимистичнее реального результата (запрос пользователя: виджет должен считать так же, как
        // считает реальный тик, а не только по зарплате и рыночным ценам).
        var overheadCost = fixedCostPerTurn + result.OutputQuantity * electricityConsumptionPerOutputUnit * market.ElectricityPrice;
        var profit = revenue - inputCost - wageCost - overheadCost;

        estimate = new FactoryProfitabilityEstimate(
            result.OutputQuantity, result.CapacityLimitedOutputQuantity, revenue, inputCost, wageCost, overheadCost, profit,
            HasPriceSignal: true);
        return true;
    }
}
