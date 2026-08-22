using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine.Tests;

public class FactoryProfitabilityCalculatorTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Material Coal = new("coal", "Уголь", SectorA, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    private static readonly Recipe SheetFromOreAndCoal = new(
        "sheet-from-ore-and-coal", Sheet, outputQuantity: 1m,
        inputs: new[] { new RecipeInput(Ore, 2m), new RecipeInput(Coal, 1m) },
        productionRate: 1m);

    private static readonly FactoryDefinition Mill =
        new("steel-mill", "Сталелитейный завод", SectorA, new[] { SheetFromOreAndCoal });

    private static readonly WorkerProductivityConfig Productivity = new()
    {
        BaseWorkerCount = 5,
        DiminishingReturnsFactor = 0.5m,
        HireCostPerWorker = 100m,
        FireCostPerWorker = 50m,
        SalaryPerWorkerPerTurn = 5m,
    };

    private static readonly RndConfig NoRndBonus = new()
    {
        ResearchPointThresholdsByLevel = Array.Empty<decimal>(),
        DiminishingReturnsExponent = 1m,
        ProductionRateBonusPerLevel = 0m,
        MaxCommitmentPerTurn = 1000m,
    };

    private static Factory NewFactory(int workers)
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, Mill);
        factory.Hire(workers);
        return factory;
    }

    /// <summary>
    /// Склад, чья реальная себестоимость (<see cref="Warehouse.AverageCostOf"/>) намеренно совпадает
    /// с рыночной ценой руды/угля этих тестов (2 и 1 соответственно) — так большинству тестов этого
    /// файла (про выручку, зарплату, мощность, накладные, распределение дефицита) не важно, какую из
    /// двух величин на самом деле берёт калькулятор для InputCost, их значения совпадают. Тест на
    /// реальное отличие (себестоимость ≠ рыночная цена) — TryCalculate_Prices_Consumed_Inputs_At_...
    /// ниже, там себестоимость задаётся явно другой.
    /// </summary>
    private static Warehouse WarehouseWith(decimal ore, decimal coal)
    {
        var warehouse = new Warehouse();
        warehouse.Add(Ore, ore, ore * 2m);
        warehouse.Add(Coal, coal, coal * 1m);
        return warehouse;
    }

    [Fact]
    public void TryCalculate_Reports_Profit_When_Output_Price_Exceeds_Inputs_And_Wages()
    {
        var factory = NewFactory(workers: 5); // 5 листов/тик: 10 руды, 5 угля
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 0m);

        var found = FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.True(found);
        Assert.True(estimate.HasPriceSignal);
        Assert.Equal(5m, estimate.ProjectedOutputQuantity);
        Assert.Equal(5m, estimate.CapacityLimitedOutputQuantity);
        Assert.Equal(50m, estimate.Revenue); // 5 листов * 10
        Assert.Equal(25m, estimate.InputCost); // 10 руды*2 + 5 угля*1
        Assert.Equal(25m, estimate.WageCost); // 5 рабочих * 5
        Assert.Equal(0m, estimate.Profit); // 50 - 25 - 25
        Assert.Equal(10m, estimate.UnitCost); // (25 + 25 + 0) / 5
        Assert.Equal(10m, estimate.OutputPrice);
    }

    [Fact]
    public void TryCalculate_Reports_A_Loss_When_Wages_Outweigh_The_Margin()
    {
        var factory = NewFactory(workers: 5);
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(4m, 1000m), // ниже входов+зарплаты
        }, electricityPrice: 0m);

        FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.True(estimate.Profit < 0m);
    }

    [Fact]
    public void TryCalculate_Marks_The_Estimate_As_Raw_Material_Limited_When_Stock_Runs_Short()
    {
        var factory = NewFactory(workers: 5); // хочет 10 руды, есть только 4
        var warehouse = WarehouseWith(ore: 4m, coal: 1000m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 0m);

        FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.Equal(5m, estimate.CapacityLimitedOutputQuantity);
        Assert.True(estimate.ProjectedOutputQuantity < estimate.CapacityLimitedOutputQuantity);
        Assert.Equal(2m, estimate.ProjectedOutputQuantity); // 4 руды / 2 на лист

        // «Максимум за ход» (запрос пользователя) — по полному рецепту по потолку мощности (5 листов
        // = 10 руды + 5 угля), а не по фактически потреблённому (4 руды из-за нехватки).
        Assert.Equal(50m, estimate.MaxRevenue); // 5 * 10
        Assert.Equal(25m, estimate.MaxInputCost); // 10 руды*2 + 5 угля*1
        Assert.Equal(0m, estimate.MaxProfit); // 50 - 25 - 25 (зарплата) - 0 (без капитальных затрат)
        Assert.Equal(10m, estimate.MaxUnitCost); // (25 + 25 + 0) / 5
    }

    [Fact]
    public void TryCalculate_Breaks_Down_Input_Cost_By_Material_With_Quantity_And_Unit_Price()
    {
        // Запрос пользователя: «добавь в таблицу цену закупки за единицу и сколько единиц купили —
        // чтобы чётко видеть прослеживаемость цены» — InputBreakdown/MaxInputBreakdown должны давать
        // ровно то, из чего складываются InputCost/MaxInputCost (по одной строке на материал).
        var factory = NewFactory(workers: 5); // 5 листов/тик: 10 руды, 5 угля
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 0m);

        FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.Equal(2, estimate.InputBreakdown.Count);
        var ore = Assert.Single(estimate.InputBreakdown, line => line.Material == Ore);
        Assert.Equal(10m, ore.Quantity);
        Assert.Equal(2m, ore.UnitCost);
        Assert.Equal(20m, ore.Cost);
        var coal = Assert.Single(estimate.InputBreakdown, line => line.Material == Coal);
        Assert.Equal(5m, coal.Quantity);
        Assert.Equal(1m, coal.UnitCost);
        Assert.Equal(5m, coal.Cost);
        Assert.Equal(estimate.InputCost, estimate.InputBreakdown.Sum(line => line.Cost));
        Assert.Equal(estimate.MaxInputCost, estimate.MaxInputBreakdown.Sum(line => line.Cost));
    }

    [Fact]
    public void TryCalculate_Returns_False_When_The_Output_Has_No_Market_Quote_Yet()
    {
        var factory = NewFactory(workers: 5);
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market(); // ни одной котировки

        var found = FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.False(found);
        Assert.False(estimate.HasPriceSignal);
    }

    [Fact]
    public void TryCalculate_Subtracts_Fixed_And_Output_Proportional_Overhead_From_Profit()
    {
        // Запрос пользователя: виджет должен учитывать капитальные затраты фабрики, а не только
        // сырьё и зарплату — иначе он систематически завышает реальную прибыльность.
        var factory = NewFactory(workers: 5); // 5 листов/тик: 10 руды, 5 угля
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 3m);

        FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate,
            fixedCostPerTurn: 8m, electricityConsumptionPerOutputUnit: 1m);

        // OverheadCost = 8 (капитальные) + 5 листов * 1 * 3 (энергия) = 23.
        Assert.Equal(23m, estimate.OverheadCost);
        Assert.Equal(50m - 25m - 25m - 23m, estimate.Profit); // выручка - сырьё - зарплата - содержание
    }

    [Fact]
    public void TryCalculate_Scales_The_Variable_Overhead_By_Capacity_In_The_Max_Scenario()
    {
        // Сырья хватает ровно на потолок мощности — «сейчас» и «максимум» совпадают по объёму, но
        // остаются разными величинами (запрос пользователя: явно видеть оба сценария).
        var factory = NewFactory(workers: 5);
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 3m);

        FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate,
            fixedCostPerTurn: 8m, electricityConsumptionPerOutputUnit: 1m);

        // Капацитет = проекция = 5 листов, поэтому MaxOverheadCost = OverheadCost = 23, но это два
        // независимо посчитанных числа, не переиспользование одного и того же поля.
        Assert.Equal(estimate.OverheadCost, estimate.MaxOverheadCost);
        Assert.Equal(estimate.Profit, estimate.MaxProfit);
    }

    [Fact]
    public void TryCalculate_Defaults_Overhead_To_Zero_When_Not_Provided()
    {
        // Обратная совместимость: вызывающая сторона, которая ещё не передаёт капитальные затраты,
        // получает ровно то же поведение, что и раньше.
        var factory = NewFactory(workers: 5);
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 3m);

        FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.Equal(0m, estimate.OverheadCost);
        Assert.Equal(0m, estimate.Profit); // как в TryCalculate_Reports_Profit_When_Output_Price_Exceeds_Inputs_And_Wages
    }

    [Fact]
    public void TryCalculate_Splits_Scarce_Input_Between_Team_Factories_Sharing_A_Level()
    {
        var factoryA = NewFactory(workers: 5);
        var factoryB = NewFactory(workers: 5);
        factoryA.SetAllocationShare(3m);
        factoryB.SetAllocationShare(1m);
        var warehouse = WarehouseWith(ore: 8m, coal: 1000m); // дефицит руды: хотят по 10, есть 8
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 0m);
        var teamFactories = new[] { factoryA, factoryB };

        FactoryProfitabilityCalculator.TryCalculate(
            factoryA, teamFactories, warehouse, market, Productivity, NoRndBonus,
            out var estimateA);
        FactoryProfitabilityCalculator.TryCalculate(
            factoryB, teamFactories, warehouse, market, Productivity, NoRndBonus,
            out var estimateB);

        // Доля 3:1 от 8 руды -> 6 и 2 -> 3 и 1 лист.
        Assert.Equal(3m, estimateA.ProjectedOutputQuantity);
        Assert.Equal(1m, estimateB.ProjectedOutputQuantity);
    }

    [Fact]
    public void TryCalculate_Prices_Consumed_Inputs_At_Their_Real_Average_Cost_Not_The_Market_Price()
    {
        // Пользовательский сценарий: руда добыта на собственном руднике (реальная себестоимость
        // 0.5/ед. — зарплата рудокопов), а рыночная цена руды в это же время взлетела до 50/ед.
        // (например, из-за чужих аварийных закупок) — прибыльность сталелитейного завода не должна
        // обваливаться из-за чужой рыночной цены на то, что реально почти ничего не стоило.
        var factory = NewFactory(workers: 5); // 5 листов/тик: 10 руды, 5 угля
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 1000m, 1000m * 0.5m); // реальная себестоимость руды — 0.5/ед., не рыночная
        warehouse.Add(Coal, 1000m, 1000m * 1m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(50m, 1000m), // рыночная цена руды — 50/ед., но это не то, что реально заплачено
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 0m);

        FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.Equal(10m * 0.5m + 5m * 1m, estimate.InputCost); // 10 руды*0.5 (реальная) + 5 угля*1 = 10, не 505 (по рыночной)
        Assert.Equal(50m - 10m - 25m, estimate.Profit); // выручка(50) - реальное сырьё(10) - зарплата(25) = 15, не дикий минус
    }

    [Fact]
    public void TryCalculate_Falls_Back_To_The_Market_Price_For_The_Max_Scenario_When_An_Input_Was_Never_Actually_Acquired()
    {
        var factory = NewFactory(workers: 5);
        var warehouse = new Warehouse(); // ни руды, ни угля ещё не завозили — совсем новый цех
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 0m);

        var found = FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.True(found); // рыночных котировок хватает, хоть реальной истории закупок и нет
        Assert.Equal(0m, estimate.ProjectedOutputQuantity); // сырья нет вовсе — реального выпуска нет
        Assert.Equal(0m, estimate.InputCost); // и тратить не на что
        Assert.Equal(25m, estimate.MaxInputCost); // теоретический потолок (10 руды*2 + 5 угля*1) — по рыночной цене, раз реальной истории закупок ещё нет
    }

    [Fact]
    public void TryCalculate_Returns_False_When_An_Input_Has_Neither_Real_Cost_History_Nor_A_Market_Quote()
    {
        var factory = NewFactory(workers: 5);
        var warehouse = new Warehouse(); // руды и угля ещё не было
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            // Coal сознательно без котировки
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 0m);

        var found = FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate);

        Assert.False(found);
        Assert.False(estimate.HasPriceSignal);
    }

    [Fact]
    public void TryCalculate_Uses_The_Repair_Tariff_For_A_Factorys_Own_Wage_And_Upkeep_While_Under_Repair()
    {
        // Симметричный случай — сама простаивающая фабрика тоже не должна оцениваться по обычной
        // (прогрессивной/полной) ставке: WearStep.RunRepairTurn списывает за неё плоский льготный
        // тариф (Workers * SalaryPerWorkerPerTurn * RepairSalaryRate, FixedCostPerTurn * RepairUpkeepRate),
        // не через общекомандную кривую и не по полной стоимости содержания.
        var factory = NewFactory(workers: 5);
        factory.StartRepair(conditionAtEntry: 0.15m, durationTurns: 3, outputMultiplier: 0m, salaryRate: 0.1m, upkeepRate: 0.4m, targetCondition: 0.85m);
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market();
        market.ReplaceQuotes(new Dictionary<string, MaterialQuote>
        {
            [Ore.Id] = new(2m, 1000m),
            [Coal.Id] = new(1m, 1000m),
            [Sheet.Id] = new(10m, 1000m),
        }, electricityPrice: 0m);

        FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            out var estimate,
            fixedCostPerTurn: 8m);

        Assert.Equal(5m * 5m * 0.1m, estimate.WageCost); // 2.5, не 25 (полная ставка) и не по прогрессии
        Assert.Equal(8m * 0.4m, estimate.OverheadCost); // 3.2, не 8 (полная ставка содержания)
    }
}
