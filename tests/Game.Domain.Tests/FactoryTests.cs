namespace Game.Domain.Tests;

public class FactoryTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Sector SectorB = new("B", "Нефтегазохимия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);
    private static readonly Material Rebar = new("rebar", "Арматура", SectorA, level: 2);

    // Recipe/FactoryDefinition сравниваются по ссылке (это объекты графа конфигурации, загружаемые
    // один раз за сессию), поэтому тесты переиспользуют эти экземпляры, а не создают похожие копии.
    private static readonly Recipe SheetRecipe =
        new("sheet-from-ore", Sheet, 1m, new[] { new RecipeInput(Ore, 2m) }, 1m);
    private static readonly Recipe RebarRecipe =
        new("rebar-from-sheet", Rebar, 1m, new[] { new RecipeInput(Sheet, 1m) }, 1m);

    private static readonly FactoryDefinition MultiRecipeMill =
        new("steel-mill", "Сталелитейный завод", SectorA, new[] { SheetRecipe, RebarRecipe });
    private static readonly FactoryDefinition SingleRecipeMill =
        new("basic-mill", "Базовый завод", SectorA, new[] { SheetRecipe });

    [Fact]
    public void Construction_Succeeds_And_Defaults_To_First_Recipe_And_Level_One()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Equal(0, factory.Workers);
        Assert.Equal(1, factory.Level);
        Assert.Equal(0m, factory.RndInvestment);
        Assert.Same(SheetRecipe, factory.SelectedRecipe);
    }

    [Fact]
    public void Construction_Throws_When_Id_Is_Empty()
    {
        Assert.Throws<ArgumentException>(() => new Factory(Ulid.Empty, SectorA, MultiRecipeMill));
    }

    [Fact]
    public void Construction_Throws_When_Owner_Sector_Does_Not_Match_Definition_Sector()
    {
        Assert.Throws<ArgumentException>(() => new Factory(Ulid.NewUlid(), SectorB, MultiRecipeMill));
    }

    [Fact]
    public void Construction_Throws_When_Selected_Recipe_Not_In_Definition()
    {
        Assert.Throws<ArgumentException>(() => new Factory(Ulid.NewUlid(), SectorA, SingleRecipeMill, RebarRecipe));
    }

    [Fact]
    public void Hire_Increases_Workers()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.Hire(5);

        Assert.Equal(5, factory.Workers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Hire_Throws_When_Count_Is_Not_Positive(int count)
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.Hire(count));
    }

    [Fact]
    public void Fire_Decreases_Workers()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        factory.Hire(5);

        factory.Fire(2);

        Assert.Equal(3, factory.Workers);
    }

    [Fact]
    public void Fire_Throws_When_More_Than_Current_Workers()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        factory.Hire(2);

        Assert.Throws<InvalidOperationException>(() => factory.Fire(3));
        Assert.Equal(2, factory.Workers);
    }

    [Fact]
    public void SelectRecipe_Switches_Between_Definitions_Recipes()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.SelectRecipe(RebarRecipe);

        Assert.Same(RebarRecipe, factory.SelectedRecipe);
    }

    [Fact]
    public void SelectRecipe_Throws_When_Recipe_Not_In_Definition()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);
        var foreignRecipe = new Recipe("foreign", Rebar, 1m, new[] { new RecipeInput(Sheet, 1m) }, 1m);

        Assert.Throws<ArgumentException>(() => factory.SelectRecipe(foreignRecipe));
    }

    [Fact]
    public void InvestInRnd_Accumulates()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.InvestInRnd(100m);
        factory.InvestInRnd(50m);

        Assert.Equal(150m, factory.RndInvestment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvestInRnd_Throws_When_Amount_Is_Not_Positive(decimal amount)
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        Assert.Throws<ArgumentOutOfRangeException>(() => factory.InvestInRnd(amount));
    }

    [Fact]
    public void AdvanceLevel_Increments_Level()
    {
        var factory = new Factory(Ulid.NewUlid(), SectorA, MultiRecipeMill);

        factory.AdvanceLevel();

        Assert.Equal(2, factory.Level);
    }
}
