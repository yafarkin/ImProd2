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
        CumulativeInvestmentThresholdsByLevel = Array.Empty<decimal>(),
        ProductionRateBonusPerLevel = 0m,
    };

    private static Factory NewFactory(int workers)
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, Mill);
        factory.Hire(workers);
        return factory;
    }

    private static Warehouse WarehouseWith(decimal ore, decimal coal)
    {
        var warehouse = new Warehouse();
        warehouse.Add(Ore, ore, 0m);
        warehouse.Add(Coal, coal, 0m);
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
            salaryPerWorkerPerTurn: Productivity.SalaryPerWorkerPerTurn, out var estimate);

        Assert.True(found);
        Assert.True(estimate.HasPriceSignal);
        Assert.Equal(5m, estimate.ProjectedOutputQuantity);
        Assert.Equal(5m, estimate.CapacityLimitedOutputQuantity);
        Assert.Equal(50m, estimate.Revenue); // 5 листов * 10
        Assert.Equal(25m, estimate.InputCost); // 10 руды*2 + 5 угля*1
        Assert.Equal(25m, estimate.WageCost); // 5 рабочих * 5
        Assert.Equal(0m, estimate.Profit); // 50 - 25 - 25
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
            salaryPerWorkerPerTurn: Productivity.SalaryPerWorkerPerTurn, out var estimate);

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
            salaryPerWorkerPerTurn: Productivity.SalaryPerWorkerPerTurn, out var estimate);

        Assert.Equal(5m, estimate.CapacityLimitedOutputQuantity);
        Assert.True(estimate.ProjectedOutputQuantity < estimate.CapacityLimitedOutputQuantity);
        Assert.Equal(2m, estimate.ProjectedOutputQuantity); // 4 руды / 2 на лист
    }

    [Fact]
    public void TryCalculate_Returns_False_When_The_Output_Has_No_Market_Quote_Yet()
    {
        var factory = NewFactory(workers: 5);
        var warehouse = WarehouseWith(ore: 1000m, coal: 1000m);
        var market = new Market(); // ни одной котировки

        var found = FactoryProfitabilityCalculator.TryCalculate(
            factory, new[] { factory }, warehouse, market, Productivity, NoRndBonus,
            salaryPerWorkerPerTurn: Productivity.SalaryPerWorkerPerTurn, out var estimate);

        Assert.False(found);
        Assert.False(estimate.HasPriceSignal);
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
            salaryPerWorkerPerTurn: Productivity.SalaryPerWorkerPerTurn, out var estimateA);
        FactoryProfitabilityCalculator.TryCalculate(
            factoryB, teamFactories, warehouse, market, Productivity, NoRndBonus,
            salaryPerWorkerPerTurn: Productivity.SalaryPerWorkerPerTurn, out var estimateB);

        // Доля 3:1 от 8 руды -> 6 и 2 -> 3 и 1 лист.
        Assert.Equal(3m, estimateA.ProjectedOutputQuantity);
        Assert.Equal(1m, estimateB.ProjectedOutputQuantity);
    }
}
