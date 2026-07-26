using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine.Tests;

public class ProductionCalculatorTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Material Coal = new("coal", "Уголь", SectorA, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    // Лист: 2 руды + 1 уголь -> 1 лист, скорость 1 ед. выхода за такт на единицу мощности.
    private static readonly Recipe SheetFromOreAndCoal = new(
        "sheet-from-ore-and-coal", Sheet, outputQuantity: 1m,
        inputs: new[] { new RecipeInput(Ore, 2m), new RecipeInput(Coal, 1m) },
        productionRate: 1m);

    private static readonly FactoryDefinition Mill =
        new("steel-mill", "Сталелитейный завод", SectorA, new[] { SheetFromOreAndCoal });

    // Добыча руды: рецепт без входов — сырьё не строится из других материалов, а добывается.
    private static readonly Recipe OreMining =
        new("ore-mining", Ore, outputQuantity: 1m, inputs: Array.Empty<RecipeInput>(), productionRate: 1m);

    private static readonly FactoryDefinition Mine =
        new("iron-mine", "Рудник", SectorA, new[] { OreMining });

    // Вход, не делящийся на остаток без остатка (для регресса на decimal-округление batches).
    private static readonly Recipe SheetFromOreOnly = new(
        "sheet-from-ore-only", Sheet, outputQuantity: 1m, inputs: new[] { new RecipeInput(Ore, 3m) }, productionRate: 1m);

    private static readonly FactoryDefinition MillOreOnly =
        new("steel-mill-ore-only", "Сталелитейный завод (без угля)", SectorA, new[] { SheetFromOreOnly });

    private static readonly WorkerProductivityConfig Productivity = new()
    {
        BaseWorkerCount = 5,
        DiminishingReturnsFactor = 0.5m,
        HireCostPerWorker = 100m,
        FireCostPerWorker = 50m,
        SalaryPerWorkerPerTurn = 5m,
    };

    // Нулевой бонус — большинство тестов проверяют мощность/сырьё изолированно от R&D;
    // сам бонус проверяет Calculate_Rnd_Level_Bonus_Multiplies_The_Production_Rate.
    private static readonly RndConfig NoRndBonus = new()
    {
        CumulativeInvestmentThresholdsByLevel = Array.Empty<decimal>(),
        ProductionRateBonusPerLevel = 0m,
    };

    private static Factory NewFactory(int workers)
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, Mill);
        if (workers > 0)
        {
            factory.Hire(workers);
        }
        return factory;
    }

    [Fact]
    public void Calculate_With_Abundant_Inputs_Is_Limited_Only_By_Worker_Capacity()
    {
        var factory = NewFactory(workers: 5); // == BaseWorkerCount, отдача линейная 1:1
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 1000m);
        warehouse.Add(Coal, 1000m);

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, NoRndBonus);

        Assert.Equal(5m, result.CapacityLimitedOutputQuantity); // 5 рабочих * ставка 1
        Assert.Equal(5m, result.OutputQuantity);
        Assert.Equal(10m, result.ConsumedInputs[Ore.Id]);
        Assert.Equal(5m, result.ConsumedInputs[Coal.Id]);
    }

    [Fact]
    public void Calculate_Applies_Diminishing_Returns_Above_The_Base_Worker_Count()
    {
        var factory = NewFactory(workers: 9); // 5 базовых + 4 сверх базы
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 1000m);
        warehouse.Add(Coal, 1000m);

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, NoRndBonus);

        // Эффективная мощность = 5 + 4 * 0.5 = 7 -> выход = 7 * ставка 1.
        Assert.Equal(7m, result.CapacityLimitedOutputQuantity);
        Assert.Equal(7m, result.OutputQuantity);
    }

    [Fact]
    public void Calculate_With_Partial_Input_Availability_Limits_Output_Proportionally()
    {
        var factory = NewFactory(workers: 5); // мощность позволила бы выпуск 5
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 4m); // хватит только на 2 листа (2 руды на лист)
        warehouse.Add(Coal, 1000m);

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, NoRndBonus);

        Assert.Equal(5m, result.CapacityLimitedOutputQuantity); // мощность прежняя
        Assert.Equal(2m, result.OutputQuantity); // но ограничено рудой
        Assert.Equal(4m, result.ConsumedInputs[Ore.Id]);
        Assert.Equal(2m, result.ConsumedInputs[Coal.Id]);
    }

    [Fact]
    public void Calculate_Never_Consumes_More_Input_Than_Is_Actually_In_Stock()
    {
        // 5 руды / 3 за партию — периодическая дробь; частное округляется до предела точности
        // decimal, и умножение обратно на 3 способно на исчезающую долю превысить фактический
        // остаток склада (было: Warehouse.Remove бросал на ровном месте).
        var factory = new Factory(Ulid.NewUlid(), SectorA, MillOreOnly);
        factory.Hire(5); // мощность (5) не станет узким местом рядом с сырьевым лимитом (~1.67)
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 5m);

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, NoRndBonus);

        Assert.Equal(5m, result.ConsumedInputs[Ore.Id]);
        warehouse.Remove(Ore, result.ConsumedInputs[Ore.Id]); // не должно бросить
    }

    [Fact]
    public void Calculate_With_No_Inputs_In_Stock_Produces_Nothing()
    {
        var factory = NewFactory(workers: 5);
        var warehouse = new Warehouse();

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, NoRndBonus);

        Assert.Equal(5m, result.CapacityLimitedOutputQuantity); // мощность была бы достаточной
        Assert.Equal(0m, result.OutputQuantity); // но сырья нет вовсе
        Assert.Equal(0m, result.ConsumedInputs[Ore.Id]);
        Assert.Equal(0m, result.ConsumedInputs[Coal.Id]);
    }

    [Fact]
    public void Calculate_With_No_Workers_Produces_Nothing_Regardless_Of_Inputs()
    {
        var factory = NewFactory(workers: 0);
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 1000m);
        warehouse.Add(Coal, 1000m);

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, NoRndBonus);

        Assert.Equal(0m, result.CapacityLimitedOutputQuantity);
        Assert.Equal(0m, result.OutputQuantity);
    }

    [Fact]
    public void Calculate_For_A_Raw_Material_Extraction_Recipe_Is_Limited_Only_By_Capacity()
    {
        var mine = new Factory(Ulid.NewUlid(), SectorA, Mine);
        mine.Hire(9); // 5 базовых + 4 сверх базы

        // Пустой склад — но добыче сырья он не нужен: она не расходует материалы вовсе.
        var result = ProductionCalculator.Calculate(mine, new Warehouse(), Productivity, NoRndBonus);

        Assert.Equal(7m, result.CapacityLimitedOutputQuantity); // 5 + 4 * 0.5
        Assert.Equal(7m, result.OutputQuantity);
        Assert.Empty(result.ConsumedInputs);
    }

    [Fact]
    public void Calculate_Rnd_Level_Bonus_Multiplies_The_Production_Rate()
    {
        var factory = NewFactory(workers: 5); // мощность 5
        factory.AdvanceLevel(); // уровень 2
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 1000m);
        warehouse.Add(Coal, 1000m);
        var rnd = new RndConfig
        {
            CumulativeInvestmentThresholdsByLevel = Array.Empty<decimal>(),
            ProductionRateBonusPerLevel = 0.2m, // +20% за уровень сверх первого
        };

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, rnd);

        // Базовый выход 5 (мощность * ставка 1) * бонус уровня (1 + 1*0.2) = 6.
        Assert.Equal(6m, result.CapacityLimitedOutputQuantity);
        Assert.Equal(6m, result.OutputQuantity);
    }

    [Fact]
    public void Calculate_Is_Limited_By_The_Scarcest_Of_Several_Inputs()
    {
        var factory = NewFactory(workers: 5);
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 1000m); // руды с избытком
        warehouse.Add(Coal, 1m); // угля хватит только на 1 лист

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, NoRndBonus);

        Assert.Equal(1m, result.OutputQuantity);
        Assert.Equal(2m, result.ConsumedInputs[Ore.Id]);
        Assert.Equal(1m, result.ConsumedInputs[Coal.Id]);
    }
}
