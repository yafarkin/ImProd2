namespace Game.Domain.Tests;

public class FactoryDefinitionTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Sector SectorB = new("B", "Нефтегазохимия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    [Fact]
    public void Construction_Succeeds_When_All_Recipes_Match_Sector()
    {
        var recipe = new Recipe("sheet-from-ore", Sheet, 1m, new[] { new RecipeInput(Ore, 2m) }, 1m);

        var factory = new FactoryDefinition("steel-mill", "Сталелитейный завод", SectorA, new[] { recipe });

        Assert.Equal(SectorA, factory.Sector);
        Assert.Single(factory.Recipes);
    }

    [Fact]
    public void Construction_Throws_When_Recipe_Sector_Does_Not_Match_Factory_Sector()
    {
        var plastic = new Material("plastic", "Пластик", SectorB, level: 1);
        var recipe = new Recipe("plastic-from-ore", plastic, 1m, new[] { new RecipeInput(Ore, 1m) }, 1m);

        Assert.Throws<ArgumentException>(() =>
            new FactoryDefinition("mismatch", "Несоответствие", SectorA, new[] { recipe }));
    }

    [Fact]
    public void Construction_Throws_When_No_Recipes()
    {
        Assert.Throws<ArgumentException>(() =>
            new FactoryDefinition("empty", "Пустая фабрика", SectorA, Array.Empty<Recipe>()));
    }
}
