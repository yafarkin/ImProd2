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
        ResearchPointThresholdsByLevel = Array.Empty<decimal>(),
        DiminishingReturnsExponent = 1m,
        ProductionRateBonusPerLevel = 0m,
        MaxCommitmentPerTurn = 1000m,
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
        warehouse.Add(Ore, 1000m, 0m);
        warehouse.Add(Coal, 1000m, 0m);

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
        warehouse.Add(Ore, 1000m, 0m);
        warehouse.Add(Coal, 1000m, 0m);

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
        warehouse.Add(Ore, 4m, 0m); // хватит только на 2 листа (2 руды на лист)
        warehouse.Add(Coal, 1000m, 0m);

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
        warehouse.Add(Ore, 5m, 0m);

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
        warehouse.Add(Ore, 1000m, 0m);
        warehouse.Add(Coal, 1000m, 0m);

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
        warehouse.Add(Ore, 1000m, 0m);
        warehouse.Add(Coal, 1000m, 0m);
        var rnd = new RndConfig
        {
            ResearchPointThresholdsByLevel = Array.Empty<decimal>(),
            DiminishingReturnsExponent = 1m,
            ProductionRateBonusPerLevel = 0.2m, // +20% за уровень сверх первого
            MaxCommitmentPerTurn = 1000m,
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
        warehouse.Add(Ore, 1000m, 0m); // руды с избытком
        warehouse.Add(Coal, 1m, 0m); // угля хватит только на 1 лист

        var result = ProductionCalculator.Calculate(factory, warehouse, Productivity, NoRndBonus);

        Assert.Equal(1m, result.OutputQuantity);
        Assert.Equal(2m, result.ConsumedInputs[Ore.Id]);
        Assert.Equal(1m, result.ConsumedInputs[Coal.Id]);
    }

    [Fact]
    public void CalculateGroup_With_No_Shared_Inputs_Matches_Independent_Calculate_Calls()
    {
        var mill = NewFactory(workers: 5); // потребляет руду + уголь
        var mine = new Factory(Ulid.NewUlid(), SectorA, Mine); // добывает руду, ничего не потребляет
        mine.Hire(5);
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 1000m, 0m);
        warehouse.Add(Coal, 1000m, 0m);

        var results = ProductionCalculator.CalculateGroup(new[] { mill, mine }, warehouse, Productivity, NoRndBonus);

        var millResult = results.Single(r => r.FactoryId == mill.Id);
        var mineResult = results.Single(r => r.FactoryId == mine.Id);
        Assert.Equal(5m, millResult.OutputQuantity); // как в Calculate_With_Abundant_Inputs...
        Assert.Equal(5m, mineResult.OutputQuantity); // 5 рабочих == BaseWorkerCount, без диминишинга
    }

    [Fact]
    public void CalculateGroup_Splits_A_Scarce_Shared_Input_By_Equal_Default_Shares()
    {
        // Обе фабрики хотят руду: Mill — 2 руды на лист (5 листов = 10 руды), MillOreOnly — 3 руды
        // на лист (5 листов = 15 руды). Суммарно хотят 25, на складе только 10 — дефицит, доли по
        // умолчанию равны (1 и 1), значит делят поровну: по 5 руды каждой.
        var mill = NewFactory(workers: 5);
        var millOreOnly = new Factory(Ulid.NewUlid(), SectorA, MillOreOnly);
        millOreOnly.Hire(5);
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 10m, 0m);
        warehouse.Add(Coal, 1000m, 0m); // уголь не в дефиците — конкуренция только за руду

        var results = ProductionCalculator.CalculateGroup(new[] { mill, millOreOnly }, warehouse, Productivity, NoRndBonus);

        var millResult = results.Single(r => r.FactoryId == mill.Id);
        var oreOnlyResult = results.Single(r => r.FactoryId == millOreOnly.Id);
        Assert.Equal(5m, millResult.ConsumedInputs[Ore.Id]);
        Assert.Equal(5m, oreOnlyResult.ConsumedInputs[Ore.Id]);
        Assert.Equal(10m, millResult.ConsumedInputs[Ore.Id] + oreOnlyResult.ConsumedInputs[Ore.Id]); // ровно весь склад, не больше
    }

    [Fact]
    public void CalculateGroup_Splits_A_Scarce_Shared_Input_Proportionally_To_Custom_Shares()
    {
        // Доли 40/60 подобраны так, чтобы квоты (4 и 6) делились на норму входа каждой фабрики (2 и
        // 3 руды/партию) без остатка — иначе неточность decimal-деления партий на входе и обратного
        // умножения дала бы 3.9999...9 вместо 4 (см. соседний тест на этот эффект уже подтверждённым
        // remainder-сценарием в Calculate_Never_Consumes_More_Input_Than_Is_Actually_In_Stock).
        var mill = NewFactory(workers: 5);
        mill.SetAllocationShare(40m);
        var millOreOnly = new Factory(Ulid.NewUlid(), SectorA, MillOreOnly);
        millOreOnly.Hire(5);
        millOreOnly.SetAllocationShare(60m);
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 10m, 0m);
        warehouse.Add(Coal, 1000m, 0m);

        var results = ProductionCalculator.CalculateGroup(new[] { mill, millOreOnly }, warehouse, Productivity, NoRndBonus);

        var millResult = results.Single(r => r.FactoryId == mill.Id);
        var oreOnlyResult = results.Single(r => r.FactoryId == millOreOnly.Id);
        Assert.Equal(4m, millResult.ConsumedInputs[Ore.Id]); // 40% от 10
        Assert.Equal(6m, oreOnlyResult.ConsumedInputs[Ore.Id]); // 60% от 10
    }

    [Fact]
    public void CalculateGroup_Does_Not_Cap_A_Contender_Below_Its_Own_Need_Just_Because_Others_Have_Share()
    {
        // MillOreOnly хочет всего 3 руды (мощность искусственно урезана 1 рабочим), Mill хочет 10;
        // хотя доли равны, MillOreOnly не может забрать больше, чем реально нужно — остаток идёт
        // Mill'у не автоматически (это заявленное упрощение — квота не возвращается, но Mill и не
        // должен получить МЕНЬШЕ своей доли (5) из-за того, что сосед не выбрал свою.
        var mill = NewFactory(workers: 5); // хочет 10 руды
        var millOreOnly = new Factory(Ulid.NewUlid(), SectorA, MillOreOnly);
        millOreOnly.Hire(1); // мощность 1 -> 1 партия -> 3 руды желаемо
        var warehouse = new Warehouse();
        warehouse.Add(Ore, 10m, 0m);
        warehouse.Add(Coal, 1000m, 0m);

        var results = ProductionCalculator.CalculateGroup(new[] { mill, millOreOnly }, warehouse, Productivity, NoRndBonus);

        var oreOnlyResult = results.Single(r => r.FactoryId == millOreOnly.Id);
        Assert.Equal(3m, oreOnlyResult.ConsumedInputs[Ore.Id]); // взял ровно сколько хотел, не больше своей квоты (5)
    }

    [Fact]
    public void CalculateCapacityBreakdown_At_Base_Worker_Count_And_Level_One_Is_Just_Workers_Times_Rate()
    {
        var factory = NewFactory(workers: 5); // == BaseWorkerCount, отдача линейная 1:1, уровень 1

        var breakdown = ProductionCalculator.CalculateCapacityBreakdown(factory, Productivity, NoRndBonus);

        Assert.Equal(5, breakdown.Workers);
        Assert.Equal(5m, breakdown.EffectiveCapacity);
        Assert.Equal(1, breakdown.Level);
        Assert.Equal(1m, breakdown.LevelBonus);
        Assert.Equal(1m, breakdown.RecipeProductionRate);
        Assert.Equal(5m, breakdown.TheoreticalMaxOutput);
    }

    [Fact]
    public void CalculateCapacityBreakdown_Applies_Diminishing_Returns_Above_Base_Worker_Count_And_The_Rnd_Level_Bonus()
    {
        var factory = NewFactory(workers: 8); // 5 базовых + 3 сверх базы
        factory.AdvanceLevel(); // уровень 2
        var rnd = new RndConfig
        {
            ResearchPointThresholdsByLevel = Array.Empty<decimal>(),
            DiminishingReturnsExponent = 1m,
            ProductionRateBonusPerLevel = 0.2m, // +20% за уровень сверх первого
            MaxCommitmentPerTurn = 1000m,
        };

        var breakdown = ProductionCalculator.CalculateCapacityBreakdown(factory, Productivity, rnd);

        // Мощность: 5 (база) + 3*0.5 (убывающая отдача) = 6.5.
        Assert.Equal(6.5m, breakdown.EffectiveCapacity);
        Assert.Equal(2, breakdown.Level);
        Assert.Equal(1.2m, breakdown.LevelBonus);
        Assert.Equal(1m, breakdown.RecipeProductionRate);
        // Потолок: ставка (1) * бонус уровня (1.2) * мощность (6.5) = 7.8.
        Assert.Equal(7.8m, breakdown.TheoreticalMaxOutput);
    }
}
