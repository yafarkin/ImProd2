namespace Game.Domain.Tests;

public class TeamTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Sector SectorB = new("B", "Нефтегазохимия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);
    private static readonly Material Plastic = new("plastic", "Пластик", SectorB, level: 1);

    private static readonly Recipe SheetRecipe =
        new("sheet-from-ore", Sheet, 1m, new[] { new RecipeInput(Ore, 2m) }, 1m);
    private static readonly Recipe PlasticRecipe =
        new("plastic-from-ore", Plastic, 1m, new[] { new RecipeInput(Ore, 1m) }, 1m);

    private static readonly FactoryDefinition SteelMill =
        new("steel-mill", "Сталелитейный завод", SectorA, new[] { SheetRecipe });
    private static readonly FactoryDefinition PlasticPlant =
        new("plastic-plant", "Нефтехимический завод", SectorB, new[] { PlasticRecipe });

    [Fact]
    public void Construction_Throws_When_Id_Is_Empty()
    {
        Assert.Throws<ArgumentException>(() => new Team(Ulid.Empty, "Команда А1", SectorA));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Construction_Throws_When_Name_Is_Empty(string name)
    {
        Assert.Throws<ArgumentException>(() => new Team(Ulid.NewUlid(), name, SectorA));
    }

    [Fact]
    public void Construction_Starts_With_No_Factories_And_Empty_Warehouse()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        Assert.Empty(team.Factories);
        Assert.Empty(team.Warehouse.Stock);
    }

    [Fact]
    public void BuildFactory_Adds_Factory_Of_Own_Sector()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        var factory = team.BuildFactory(Ulid.NewUlid(), SteelMill);

        Assert.Same(factory, Assert.Single(team.Factories));
        Assert.Equal(SteelMill, factory.Definition);
    }

    [Fact]
    public void BuildFactory_Throws_When_Definition_Sector_Differs_From_Team_Sector()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        Assert.Throws<ArgumentException>(() => team.BuildFactory(Ulid.NewUlid(), PlasticPlant));
        Assert.Empty(team.Factories);
    }
}
