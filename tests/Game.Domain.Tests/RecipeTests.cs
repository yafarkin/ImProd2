namespace Game.Domain.Tests;

public class RecipeTests
{
    private static readonly Sector Sector = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", Sector, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", Sector, level: 1);

    [Fact]
    public void Construction_Succeeds_And_Exposes_Direct_Inputs()
    {
        var recipe = new Recipe(
            id: "sheet-from-ore",
            output: Sheet,
            outputQuantity: 1m,
            inputs: new[] { new RecipeInput(Ore, 2m) },
            productionRate: 1m);

        Assert.Equal(Sheet, recipe.Output);
        Assert.Equal(new[] { Ore }, recipe.DirectInputMaterials);
    }

    [Fact]
    public void Construction_Throws_When_A_Non_Raw_Materials_Inputs_Are_Empty()
    {
        Assert.Throws<ArgumentException>(() =>
            new Recipe("bad", Sheet, 1m, Array.Empty<RecipeInput>(), 1m));
    }

    [Fact]
    public void Construction_Throws_When_A_Raw_Materials_Recipe_Has_Inputs()
    {
        var anotherOre = new Material("ore2", "Руда редких металлов", Sector, level: 0);

        Assert.Throws<ArgumentException>(() =>
            new Recipe("bad", Ore, 1m, new[] { new RecipeInput(anotherOre, 1m) }, 1m));
    }

    [Fact]
    public void Construction_Succeeds_For_A_Raw_Material_Mined_Without_Any_Inputs()
    {
        var recipe = new Recipe("ore-mining", Ore, outputQuantity: 1m, inputs: Array.Empty<RecipeInput>(), productionRate: 1m);

        Assert.Equal(Ore, recipe.Output);
        Assert.Empty(recipe.DirectInputMaterials);
    }

    [Fact]
    public void Construction_Throws_When_Output_Is_Its_Own_Input()
    {
        Assert.Throws<ArgumentException>(() =>
            new Recipe("bad", Sheet, 1m, new[] { new RecipeInput(Sheet, 1m) }, 1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construction_Throws_When_Output_Quantity_Is_Not_Positive(decimal outputQuantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Recipe("bad", Sheet, outputQuantity, new[] { new RecipeInput(Ore, 1m) }, 1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construction_Throws_When_Production_Rate_Is_Not_Positive(decimal productionRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Recipe("bad", Sheet, 1m, new[] { new RecipeInput(Ore, 1m) }, productionRate));
    }

    [Fact]
    public void Chain_Ore_To_Sheet_To_Nail_Decomposes_To_Direct_Inputs_Only()
    {
        var nail = new Material("nail", "Гвозди", Sector, level: 2);

        var sheetRecipe = new Recipe(
            id: "sheet-from-ore",
            output: Sheet,
            outputQuantity: 1m,
            inputs: new[] { new RecipeInput(Ore, 2m) },
            productionRate: 1m);

        var nailRecipe = new Recipe(
            id: "nail-from-sheet",
            output: nail,
            outputQuantity: 10m,
            inputs: new[] { new RecipeInput(Sheet, 3m) },
            productionRate: 2m);

        Assert.Equal(new[] { Ore }, sheetRecipe.DirectInputMaterials);
        Assert.Equal(new[] { Sheet }, nailRecipe.DirectInputMaterials);
        Assert.DoesNotContain(Ore, nailRecipe.DirectInputMaterials);
    }
}
