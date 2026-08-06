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
    /// <see cref="UnitCost"/> — реальная себестоимость единицы при <see cref="ProjectedOutputQuantity"/>
    /// (сырьё + зарплата + содержание, делённые на выпуск; 0, если выпуска нет — делить не на что).
    /// Величины с префиксом <c>Max</c> — та же оценка, но при <see cref="CapacityLimitedOutputQuantity"/>
    /// (запрос пользователя: «сколько фабрика сможет в теории максимум заработать за текущий ход», то
    /// есть если бы нехватки сырья не было вовсе) — зарплата и капитальная часть содержания не растут
    /// с объёмом выпуска, поэтому в <see cref="MaxProfit"/> они те же, что и в <see cref="Profit"/>.
    /// <see cref="OutputPrice"/> — текущая рыночная цена продукта за единицу (запрос пользователя:
    /// видеть её рядом с <see cref="UnitCost"/>, чтобы сразу было видно, продаётся ли товар выше или
    /// ниже себестоимости) — 0, если рыночной котировки ещё нет (<see cref="HasPriceSignal"/> false).
    /// </summary>
    public sealed record FactoryProfitabilityEstimate(
        decimal ProjectedOutputQuantity,
        decimal CapacityLimitedOutputQuantity,
        decimal Revenue,
        decimal InputCost,
        decimal WageCost,
        decimal OverheadCost,
        decimal Profit,
        decimal UnitCost,
        decimal OutputPrice,
        decimal MaxRevenue,
        decimal MaxInputCost,
        decimal MaxOverheadCost,
        decimal MaxUnitCost,
        decimal MaxProfit,
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
        WorkerProductivityConfig productivity, RndConfig rnd,
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
        var recipe = factory.SelectedRecipe;

        // Котировка нужна и на выход, и на КАЖДЫЙ вход рецепта — не только на фактически потреблённые
        // (result.ConsumedInputs может быть меньше полного рецепта или вовсе пустым при простое из-за
        // нехватки сырья), потому что MaxInputCost ниже считается по полному рецепту, а не по факту.
        if (!market.HasQuote(recipe.Output.Id) || recipe.Inputs.Any(input => !market.HasQuote(input.Material.Id)))
        {
            estimate = NoPriceSignalEstimate(result);
            return false;
        }

        var inputCost = 0m;
        foreach (var (materialId, quantity) in result.ConsumedInputs)
        {
            inputCost += quantity * market.QuoteOf(materialId).Price;
        }

        var outputPrice = market.QuoteOf(recipe.Output.Id).Price;
        var revenue = result.OutputQuantity * outputPrice;
        // Зарплата теперь прогрессивная и считается на всю команду сразу (см.
        // FinanceCalculator.CalculateSalaries) — этой фабрике достаётся пропорциональная её доле
        // рабочих часть общей суммы, а не отдельная плоская ставка (иначе оценка виджета
        // разойдётся с тем, что реально спишет TickFinanceStep).
        var totalTeamWorkers = teamFactories.Sum(f => f.Workers);
        var totalTeamSalary = FinanceCalculator.CalculateSalaries(totalTeamWorkers, productivity);
        var wageCost = totalTeamWorkers > 0 ? totalTeamSalary * factory.Workers / totalTeamWorkers : 0m;
        // Те же два слагаемых, что реально списываются с баланса при настоящем производстве (см.
        // FactoryUpkeepPaid и FactoryProduced.OverheadCost) — иначе оценка тут была бы систематически
        // оптимистичнее реального результата (запрос пользователя: виджет должен считать так же, как
        // считает реальный тик, а не только по зарплате и рыночным ценам).
        var overheadCost = fixedCostPerTurn + result.OutputQuantity * electricityConsumptionPerOutputUnit * market.ElectricityPrice;
        var profit = revenue - inputCost - wageCost - overheadCost;
        var unitCost = result.OutputQuantity > 0 ? (inputCost + wageCost + overheadCost) / result.OutputQuantity : 0m;

        // «Максимум за ход» (запрос пользователя) — та же оценка, но при CapacityLimitedOutputQuantity
        // (потолок по рабочим/уровню фабрики, без учёта дефицита сырья, см. doc-comment ProductionResult):
        // сколько нужно было бы сырья на весь этот потолок по рецепту (не по факту потреблённого),
        // умноженное на его рыночную цену. Зарплата не входит — она зависит от числа рабочих, а не от
        // выпуска, и уже посчитана выше.
        var batchesAtCapacity = recipe.OutputQuantity > 0 ? result.CapacityLimitedOutputQuantity / recipe.OutputQuantity : 0m;
        var maxInputCost = recipe.Inputs.Sum(input => batchesAtCapacity * input.Quantity * market.QuoteOf(input.Material.Id).Price);
        var maxRevenue = result.CapacityLimitedOutputQuantity * outputPrice;
        var maxOverheadCost = fixedCostPerTurn + result.CapacityLimitedOutputQuantity * electricityConsumptionPerOutputUnit * market.ElectricityPrice;
        var maxProfit = maxRevenue - maxInputCost - wageCost - maxOverheadCost;
        var maxUnitCost = result.CapacityLimitedOutputQuantity > 0
            ? (maxInputCost + wageCost + maxOverheadCost) / result.CapacityLimitedOutputQuantity
            : 0m;

        estimate = new FactoryProfitabilityEstimate(
            result.OutputQuantity, result.CapacityLimitedOutputQuantity, revenue, inputCost, wageCost, overheadCost, profit,
            unitCost, outputPrice, maxRevenue, maxInputCost, maxOverheadCost, maxUnitCost, maxProfit,
            HasPriceSignal: true);
        return true;
    }

    private static FactoryProfitabilityEstimate NoPriceSignalEstimate(ProductionResult result) => new(
        result.OutputQuantity, result.CapacityLimitedOutputQuantity,
        Revenue: 0m, InputCost: 0m, WageCost: 0m, OverheadCost: 0m, Profit: 0m, UnitCost: 0m, OutputPrice: 0m,
        MaxRevenue: 0m, MaxInputCost: 0m, MaxOverheadCost: 0m, MaxUnitCost: 0m, MaxProfit: 0m,
        HasPriceSignal: false);
}
