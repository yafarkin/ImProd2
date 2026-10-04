using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Web.Tests;

/// <summary>
/// Цепочка раздела «Производство» (docs/manager-ui/README.md §4, блок 3): переделы, однотипные
/// фабрики одной группой, склад после передела, который его производит, и подписи к ним.
/// </summary>
public class ProductionChainTests
{
    private static readonly IReadOnlyDictionary<Ulid, decimal> NoOutputs = new Dictionary<Ulid, decimal>();
    private static readonly IReadOnlyDictionary<Ulid, IReadOnlyDictionary<string, decimal>> NoConsumption =
        new Dictionary<Ulid, IReadOnlyDictionary<string, decimal>>();
    private static readonly IReadOnlyDictionary<Ulid, IReadOnlyList<string>> NoProblems = new Dictionary<Ulid, IReadOnlyList<string>>();

    /// <summary>Маленький неизменный каталог: в секторе A рудник (передел 0), сталелитейный завод (1) и прокатный стан (2).</summary>
    private static readonly ResolvedGameConfig Config = GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    private static readonly Sector SectorA = Config.Sectors.First();
    private static readonly IReadOnlyList<FactoryDefinition> SectorDefinitions = Config.FactoryDefinitions.Where(d => d.Sector.Id == SectorA.Id).ToList();
    private static FactoryDefinition Mine => SectorDefinitions.Single(d => ProductionChain.LevelOf(d) == 0);
    private static FactoryDefinition Mill => SectorDefinitions.Single(d => ProductionChain.LevelOf(d) == 1);
    private static Material Ore => Mine.Recipes[0].Output;
    private static Material Sheet => Mill.Recipes[0].Output;

    private static Factory NewFactory(FactoryDefinition definition) => new(Ulid.NewUlid(), SectorA, definition);

    private static ProductionChain.Chain Build(
        IReadOnlyList<Factory> factories,
        int unlockedGeneration = 2,
        IReadOnlyList<(Material, decimal)>? stock = null,
        IReadOnlyDictionary<Ulid, IReadOnlyList<string>>? problems = null,
        IReadOnlyDictionary<Ulid, decimal>? produced = null,
        IReadOnlyDictionary<Ulid, IReadOnlyDictionary<string, decimal>>? consumed = null)
    {
        var nodes = FactoryOverviewList.Build(SectorDefinitions, factories, NoOutputs, NoOutputs);
        var names = factories
            .GroupBy(f => f.Definition.Id)
            .SelectMany(group => group.OrderBy(f => f.Id).Select((f, index) => (f.Id, Name: index > 0 ? $"{f.Definition.Name} №{index + 1}" : f.Definition.Name)))
            .ToDictionary(x => x.Id, x => x.Name);
        return ProductionChain.Build(
            nodes, names, problems ?? NoProblems, stock ?? [], produced ?? NoOutputs, consumed ?? NoConsumption, unlockedGeneration);
    }

    [Fact]
    public void Same_Type_Factories_Become_One_Group_Inside_Their_Level()
    {
        var chain = Build([NewFactory(Mine), NewFactory(Mine), NewFactory(Mill)]);

        var level0 = chain.Levels.Single(l => l.Number == 0);
        var mines = level0.Types.Single(t => t.Definition == Mine);
        Assert.Equal(2, mines.Instances.Count);
        Assert.Equal(["Рудник", "Рудник №2"], mines.Instances.Select(i => i.Title).Order());
        Assert.Equal(2, level0.FactoryCount);
        Assert.Equal(3, chain.FactoryCount);
    }

    [Fact]
    public void Levels_Closed_By_Generation_Research_Become_One_Lock_Not_Empty_Levels()
    {
        var chain = Build([NewFactory(Mine)], unlockedGeneration: 1);

        Assert.Equal([0, 1], chain.Levels.Select(l => l.Number));
        Assert.NotNull(chain.Locked);
        Assert.Equal(2, chain.Locked!.FirstLevel);
        Assert.Equal(["Прокатный стан"], chain.Locked.TypeNames);
    }

    [Fact]
    public void Nothing_Is_Locked_Once_Every_Level_Is_Open()
    {
        Assert.Null(Build([], unlockedGeneration: 2).Locked);
    }

    [Fact]
    public void Stock_Stands_After_The_Level_That_Produces_It()
    {
        var chain = Build([NewFactory(Mine), NewFactory(Mill)], stock: [(Ore, 120m), (Sheet, 7m)]);

        Assert.Equal([Ore.Id], chain.Levels.Single(l => l.Number == 0).StockLines.Select(s => s.Material.Id));
        Assert.Equal([Sheet.Id], chain.Levels.Single(l => l.Number == 1).StockLines.Select(s => s.Material.Id));
        Assert.Empty(chain.OtherStock);
    }

    [Fact]
    public void A_Material_No_Sector_Factory_Produces_Goes_To_Other_Stock_Even_With_The_Same_Level_Number()
    {
        // Нефть другого сектора — тоже передел 0, но на склад рудника ей не место: куплена по контракту.
        var oil = Config.Materials.Values.Single(m => m.Name == "Нефть");
        Assert.Equal(Ore.Level, oil.Level);

        var chain = Build([NewFactory(Mine)], stock: [(oil, 5m)]);

        Assert.Equal([oil.Id], chain.OtherStock.Select(s => s.Material.Id));
        Assert.DoesNotContain(chain.Levels.SelectMany(l => l.StockLines), s => s.Material.Id == oil.Id);
    }

    [Fact]
    public void The_Product_Of_A_Built_Factory_Shows_Even_With_Nothing_Left_On_Stock()
    {
        // «+1700 · −1700» — всё забрали сразу; это ответ, а не повод спрятать строку.
        var chain = Build([NewFactory(Mine)]);

        var line = Assert.Single(chain.Levels.Single(l => l.Number == 0).StockLines);
        Assert.Equal(Ore.Id, line.Material.Id);
        Assert.Equal(0m, line.Quantity);
    }

    [Fact]
    public void Stock_Line_Knows_Who_Produced_And_Who_Took_How_Much_Last_Turn()
    {
        var mine = NewFactory(Mine);
        var mill = NewFactory(Mill);

        var chain = Build(
            [mine, mill], stock: [(Ore, 30m)],
            produced: new Dictionary<Ulid, decimal> { [mine.Id] = 100m, [mill.Id] = 10m },
            consumed: new Dictionary<Ulid, IReadOnlyDictionary<string, decimal>> { [mill.Id] = new Dictionary<string, decimal> { [Ore.Id] = 70m } });

        var ore = chain.Levels.Single(l => l.Number == 0).StockLines.Single();
        Assert.Equal(100m, ore.ProducedLastTurn);
        Assert.Equal([("Сталелитейный завод", 70m)], ore.ConsumedLastTurn);
        Assert.True(chain.HasLastTurn);
        Assert.Equal("+100 · −70 Сталелитейный завод", ProductionChainDisplay.FlowText(ore, chain.HasLastTurn));
    }

    [Fact]
    public void Before_The_First_Settlement_The_Flow_Names_Factories_Instead_Of_Zeros()
    {
        var chain = Build([NewFactory(Mine), NewFactory(Mill)]);

        var ore = chain.Levels.Single(l => l.Number == 0).StockLines.Single();
        Assert.False(chain.HasLastTurn);
        Assert.Equal("производит: Рудник · потребляет: Сталелитейный завод", ProductionChainDisplay.FlowText(ore, chain.HasLastTurn));
    }

    [Fact]
    public void A_Material_Nobody_Takes_Says_So()
    {
        // Живой обход 2026-10-04: руда копилась тысячами, а со склада не было видно, что её не берёт никто.
        var chain = Build([NewFactory(Mine)], stock: [(Ore, 6555m)]);

        var text = ProductionChainDisplay.FlowText(chain.Levels.Single(l => l.Number == 0).StockLines.Single(), chain.HasLastTurn);
        Assert.Contains("не потребляет ни одна ваша фабрика", text);
    }

    [Fact]
    public void Problem_Levels_Are_Open_And_Quiet_Levels_Of_A_Big_Chain_Are_Collapsed()
    {
        var mines = Enumerable.Range(0, ProductionChain.SmallChainFactoryCount).Select(_ => NewFactory(Mine)).ToList();
        var mill = NewFactory(Mill);

        var chain = Build(
            [.. mines, mill],
            problems: new Dictionary<Ulid, IReadOnlyList<string>> { [mill.Id] = ["нет рабочих"] });

        Assert.False(chain.Levels.Single(l => l.Number == 0).OpenByDefault);
        var level1 = chain.Levels.Single(l => l.Number == 1);
        Assert.True(level1.OpenByDefault);
        Assert.Equal(1, level1.ProblemCount);
        Assert.Equal(1, chain.ProblemCount);
    }

    [Fact]
    public void A_Small_Chain_Is_Fully_Open_So_A_Newcomer_Sees_The_Build_Buttons()
    {
        var chain = Build([NewFactory(Mine)]);

        Assert.All(chain.Levels, level => Assert.True(level.OpenByDefault));
    }

    [Fact]
    public void A_Type_Group_Reports_Its_Worst_Condition_And_Total_Workers()
    {
        var chain = Build([NewFactory(Mine), NewFactory(Mine)]);

        var group = chain.Levels.Single(l => l.Number == 0).Types.Single(t => t.Definition == Mine);
        Assert.Equal(1m, group.WorstCondition);
        Assert.Equal("0 рабочих · хуже всех: состояние 100 %", ProductionChainDisplay.TypeGroupSubtitle(group).Replace('\u00A0', ' '));
    }

    [Theory]
    [InlineData(1, "рабочий")]
    [InlineData(3, "рабочих")]
    [InlineData(11, "рабочих")]
    [InlineData(21, "рабочий")]
    public void Workers_Are_Declined(int count, string expected) =>
        Assert.Equal(expected, ProductionChainDisplay.Workers(count));

    [Fact]
    public void Output_Below_The_Ceiling_Shows_The_Ceiling_Too()
    {
        Assert.Equal("1108 из 1251", ProductionChainDisplay.OutputText(1108m, 1251m));
        Assert.Equal("1251", ProductionChainDisplay.OutputText(1251m, 1251m));
        Assert.Null(ProductionChainDisplay.OutputText(null, 1251m));
    }

    [Fact]
    public void A_Level_Without_Factories_Is_Summarised_As_Not_Built()
    {
        var chain = Build([NewFactory(Mine)]);

        Assert.Equal("не построено", ProductionChainDisplay.LevelSummary(chain.Levels.Single(l => l.Number == 1), chain.HasLastTurn));
        Assert.Equal("1 фабрика", ProductionChainDisplay.LevelSummary(chain.Levels.Single(l => l.Number == 0), chain.HasLastTurn));
    }
}
