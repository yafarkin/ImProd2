namespace Game.Domain.Tests;

public class RecipeBookTests
{
    private static readonly Sector Sector = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", Sector, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", Sector, level: 1);
    private static readonly Material Nail = new("nail", "Гвозди", Sector, level: 2);

    private static readonly Recipe SheetRecipe =
        new("sheet-from-ore", Sheet, 1m, new[] { new RecipeInput(Ore, 2m) }, 1m);
    private static readonly Recipe NailRecipe =
        new("nail-from-sheet", Nail, 10m, new[] { new RecipeInput(Sheet, 3m) }, 2m);

    [Fact]
    public void GetRecipe_Returns_Recipe_Producing_Material()
    {
        var book = new RecipeBook(new[] { SheetRecipe, NailRecipe });

        Assert.Same(SheetRecipe, book.GetRecipe(Sheet));
        Assert.Same(NailRecipe, book.GetRecipe(Nail));
    }

    [Fact]
    public void GetRecipe_Throws_When_No_Recipe_Produces_Material()
    {
        var book = new RecipeBook(new[] { SheetRecipe });

        Assert.Throws<KeyNotFoundException>(() => book.GetRecipe(Nail));
    }

    [Fact]
    public void TryGetRecipe_Returns_Null_When_No_Recipe_Produces_Material()
    {
        var book = new RecipeBook(new[] { SheetRecipe });

        Assert.Null(book.TryGetRecipe(Nail));
    }

    [Fact]
    public void Construction_Throws_When_Two_Recipes_Produce_Same_Material()
    {
        var duplicateSheetRecipe =
            new Recipe("sheet-from-ore-alt", Sheet, 2m, new[] { new RecipeInput(Ore, 3m) }, 1m);

        Assert.Throws<ArgumentException>(() => new RecipeBook(new[] { SheetRecipe, duplicateSheetRecipe }));
    }
}
