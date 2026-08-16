using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Оценка прибыльности одной фабрики «если бы тик прошёл прямо сейчас» (запрос пользователя «можем
/// ли понять, фабрика вообще прибыльна или нет?») — чистая функция, ничего не мутирует и не
/// пишет в журнал, по тому же принципу, что и <see cref="ProductionCalculator"/>. Выход продаётся по
/// текущей рыночной цене (<see cref="Market"/>), но вход — по его реальной средней себестоимости на
/// складе (<see cref="Warehouse.AverageCostOf"/>: зарплата и накладные, если материал добыт своей же
/// фабрикой, или реально уплаченная цена, если куплен) — не по рыночной цене, кроме единственного
/// случая, когда материал ещё вообще ни разу не приобретался (тогда рыночная цена — единственная
/// доступная оценка). Раньше вход всегда оценивался по рынку, как будто фабрика — отдельный торгующий
/// узел, покупающий сырьё заново каждый тик; это давало дикие ложные убытки для фабрик, кормящихся
/// от собственной цепочки, если рыночная цена сырья взлетала выше его реальной себестоимости
/// (запрос пользователя: «оно то поставляется мне на склад — почему себестоимость берётся не из
/// предыдущей цепочки»). Сознательно другая метрика, чем теоретическая себестоимость из пирамиды
/// сырья «с нуля» (<c>CostCalculator</c>/<c>DashboardDisplay.TryCalculateUnitCost</c>, используется в
/// другом месте страницы для другого вопроса) — эта величина про то, что реально сейчас лежит на
/// складе и сколько за это реально заплачено, усреднённо по всем поступлениям (закупка, поставка по
/// контракту, собственное производство — методом накопительной средней, см. <see
/// cref="Domain.MaterialOnStock"/>).
/// </summary>
public static class FactoryProfitabilityCalculator
{
    /// <summary>
    /// Одна строка разбивки затрат на сырьё по конкретному материалу (запрос пользователя: «давай в
    /// таблицу добавим информацию по цене закупки за единицу всего, и сколько всего мы единиц купили —
    /// чтобы чётко видеть прослеживаемость цены», т.е. из чего складывается <see
    /// cref="FactoryProfitabilityEstimate.InputCost"/>/<see
    /// cref="FactoryProfitabilityEstimate.MaxInputCost"/>, а не только итоговая сумма). <see
    /// cref="UnitCost"/> — та же реальная себестоимость (или запасной вариант — рыночная цена), что
    /// используется для расчёта <see cref="Cost"/> = <see cref="Quantity"/> × <see cref="UnitCost"/>.
    /// </summary>
    public sealed record InputCostLine(Material Material, decimal Quantity, decimal UnitCost, decimal Cost);

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
    /// <see cref="InputBreakdown"/>/<see cref="MaxInputBreakdown"/> — по строке на каждый материал
    /// рецепта, сумма их <see cref="InputCostLine.Cost"/> равна <see cref="InputCost"/>/<see
    /// cref="MaxInputCost"/> соответственно.
    /// </summary>
    public sealed record FactoryProfitabilityEstimate(
        decimal ProjectedOutputQuantity,
        decimal CapacityLimitedOutputQuantity,
        decimal Revenue,
        decimal InputCost,
        IReadOnlyList<InputCostLine> InputBreakdown,
        decimal WageCost,
        decimal OverheadCost,
        decimal Profit,
        decimal UnitCost,
        decimal OutputPrice,
        decimal MaxRevenue,
        decimal MaxInputCost,
        IReadOnlyList<InputCostLine> MaxInputBreakdown,
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

        // Котировка на выход нужна всегда (для выручки). На каждый вход рецепта (не только фактически
        // потреблённые — MaxInputCost ниже считается по полному рецепту, а не по факту) нужна ЛИБО
        // реальная себестоимость уже накопленного остатка на складе, ЛИБО рыночная котировка как
        // запасной вариант для материала, который ещё ни разу реально не приобретался (см. doc-comment
        // класса) — без ни того, ни другого оценить нечем вообще.
        if (!market.HasQuote(recipe.Output.Id)
            || recipe.Inputs.Any(input => warehouse.AverageCostOf(input.Material) <= 0m && !market.HasQuote(input.Material.Id)))
        {
            estimate = NoPriceSignalEstimate(result);
            return false;
        }

        decimal UnitCostOf(Material material)
        {
            var realCost = warehouse.AverageCostOf(material);
            return realCost > 0m ? realCost : market.QuoteOf(material.Id).Price;
        }

        var inputBreakdown = new List<InputCostLine>();
        foreach (var (materialId, quantity) in result.ConsumedInputs)
        {
            var material = recipe.Inputs.Single(input => input.Material.Id == materialId).Material;
            var lineUnitCost = UnitCostOf(material);
            inputBreakdown.Add(new InputCostLine(material, quantity, lineUnitCost, quantity * lineUnitCost));
        }
        var inputCost = inputBreakdown.Sum(line => line.Cost);

        var outputPrice = market.QuoteOf(recipe.Output.Id).Price;
        var revenue = result.OutputQuantity * outputPrice;
        // Зарплата теперь прогрессивная и считается на всю команду сразу (см.
        // FinanceCalculator.CalculateSalaries) — этой фабрике достаётся пропорциональная её доле
        // рабочих часть общей суммы, а не отдельная плоская ставка (иначе оценка виджета
        // разойдётся с тем, что реально спишет TickFinanceStep). Фабрики на вынужденном простое
        // исключены из этого пула тем же способом, что и в TickFinanceStep.Run (см. фильтр
        // !IsUnderRepair там), — их зарплата и содержание идёт не через прогрессивную кривую, а по
        // отдельному плоскому льготному тарифу (WearStep.RunRepairTurn). Раньше эта фабрика всё
        // равно попадала в totalTeamWorkers — команда с любой одной простаивающей фабрикой видела
        // завышенную (по более высокой прогрессивной ступени) зарплатную нагрузку у ВСЕХ остальных,
        // работающих фабрик, из-за чего виджет мог показать убыток там, где реально команда в плюсе
        // (найдено по жалобе пользователя: «Прибыльность фабрики» расходится с реальным балансом).
        var workingTeamFactories = teamFactories.Where(f => !f.IsUnderRepair).ToList();
        var totalTeamWorkers = workingTeamFactories.Sum(f => f.Workers);
        var totalTeamSalary = FinanceCalculator.CalculateSalaries(totalTeamWorkers, productivity);
        var wageCost = factory.IsUnderRepair
            ? factory.Workers * productivity.SalaryPerWorkerPerTurn * factory.RepairSalaryRate
            : totalTeamWorkers > 0 ? totalTeamSalary * factory.Workers / totalTeamWorkers : 0m;
        // Те же два слагаемых, что реально списываются с баланса при настоящем производстве (см.
        // FactoryUpkeepPaid и FactoryProduced.OverheadCost) — иначе оценка тут была бы систематически
        // оптимистичнее реального результата (запрос пользователя: виджет должен считать так же, как
        // считает реальный тик, а не только по зарплате и рыночным ценам). У фабрики на простое
        // содержание тоже идёт по льготному тарифу простоя (см. wageCost выше), не по полной ставке.
        var overheadCost = (factory.IsUnderRepair ? fixedCostPerTurn * factory.RepairUpkeepRate : fixedCostPerTurn)
                            + result.OutputQuantity * electricityConsumptionPerOutputUnit * market.ElectricityPrice;
        var profit = revenue - inputCost - wageCost - overheadCost;
        var unitCost = result.OutputQuantity > 0 ? (inputCost + wageCost + overheadCost) / result.OutputQuantity : 0m;

        // «Максимум за ход» (запрос пользователя) — та же оценка, но при CapacityLimitedOutputQuantity
        // (потолок по рабочим/уровню фабрики, без учёта дефицита сырья, см. doc-comment ProductionResult):
        // сколько нужно было бы сырья на весь этот потолок по рецепту (не по факту потреблённого), по
        // той же реальной себестоимости (с тем же запасным вариантом на рыночную цену), что и выше.
        // Зарплата не входит — она зависит от числа рабочих, а не от выпуска, и уже посчитана выше.
        var batchesAtCapacity = recipe.OutputQuantity > 0 ? result.CapacityLimitedOutputQuantity / recipe.OutputQuantity : 0m;
        var maxInputBreakdown = recipe.Inputs
            .Select(input =>
            {
                var quantity = batchesAtCapacity * input.Quantity;
                var lineUnitCost = UnitCostOf(input.Material);
                return new InputCostLine(input.Material, quantity, lineUnitCost, quantity * lineUnitCost);
            })
            .ToList();
        var maxInputCost = maxInputBreakdown.Sum(line => line.Cost);
        var maxRevenue = result.CapacityLimitedOutputQuantity * outputPrice;
        var maxOverheadCost = (factory.IsUnderRepair ? fixedCostPerTurn * factory.RepairUpkeepRate : fixedCostPerTurn)
                               + result.CapacityLimitedOutputQuantity * electricityConsumptionPerOutputUnit * market.ElectricityPrice;
        var maxProfit = maxRevenue - maxInputCost - wageCost - maxOverheadCost;
        var maxUnitCost = result.CapacityLimitedOutputQuantity > 0
            ? (maxInputCost + wageCost + maxOverheadCost) / result.CapacityLimitedOutputQuantity
            : 0m;

        estimate = new FactoryProfitabilityEstimate(
            result.OutputQuantity, result.CapacityLimitedOutputQuantity, revenue, inputCost, inputBreakdown, wageCost, overheadCost, profit,
            unitCost, outputPrice, maxRevenue, maxInputCost, maxInputBreakdown, maxOverheadCost, maxUnitCost, maxProfit,
            HasPriceSignal: true);
        return true;
    }

    private static FactoryProfitabilityEstimate NoPriceSignalEstimate(ProductionResult result) => new(
        result.OutputQuantity, result.CapacityLimitedOutputQuantity,
        Revenue: 0m, InputCost: 0m, InputBreakdown: Array.Empty<InputCostLine>(), WageCost: 0m, OverheadCost: 0m, Profit: 0m, UnitCost: 0m, OutputPrice: 0m,
        MaxRevenue: 0m, MaxInputCost: 0m, MaxInputBreakdown: Array.Empty<InputCostLine>(), MaxOverheadCost: 0m, MaxUnitCost: 0m, MaxProfit: 0m,
        HasPriceSignal: false);
}
