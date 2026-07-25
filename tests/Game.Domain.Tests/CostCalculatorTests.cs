namespace Game.Domain.Tests;

public class CostCalculatorTests
{
    private static readonly Sector Sector = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", Sector, level: 0);
    private static readonly Material Sheet = new("sheet", "Стальные листы", Sector, level: 1);
    private static readonly Material Nail = new("nail", "Гвозди", Sector, level: 2);

    // лист: 2 руды -> 1 лист; гвоздь: 3 листа -> 10 гвоздей.
    private static readonly Recipe SheetRecipe =
        new("sheet-from-ore", Sheet, 1m, new[] { new RecipeInput(Ore, 2m) }, 1m);
    private static readonly Recipe NailRecipe =
        new("nail-from-sheet", Nail, 10m, new[] { new RecipeInput(Sheet, 3m) }, 2m);

    private static readonly RecipeBook Book = new(new[] { SheetRecipe, NailRecipe });
    private static readonly Dictionary<Material, decimal> RawCosts = new() { [Ore] = 10m };

    [Fact]
    public void CalculateUnitCost_Raw_Material_Returns_Configured_Base_Cost()
    {
        var cost = CostCalculator.CalculateUnitCost(Ore, Book, RawCosts);

        Assert.Equal(10m, cost);
    }

    [Fact]
    public void CalculateUnitCost_Single_Level_Recipe_Divides_Input_Cost_By_Output_Quantity()
    {
        var cost = CostCalculator.CalculateUnitCost(Sheet, Book, RawCosts);

        Assert.Equal(20m, cost); // 2 ore * 10 / 1 sheet
    }

    [Fact]
    public void CalculateUnitCost_Three_Level_Chain_Recurses_Through_Intermediate_Materials()
    {
        var cost = CostCalculator.CalculateUnitCost(Nail, Book, RawCosts);

        Assert.Equal(6m, cost); // 3 sheet * 20 / 10 nail
    }

    [Fact]
    public void CalculateUnitCost_Throws_When_Raw_Material_Cost_Is_Missing()
    {
        Assert.Throws<ArgumentException>(
            () => CostCalculator.CalculateUnitCost(Ore, Book, new Dictionary<Material, decimal>()));
    }

    [Fact]
    public void CalculateUnitCost_Throws_When_Recipe_Book_Has_No_Recipe_For_Material()
    {
        var emptyBook = new RecipeBook(Array.Empty<Recipe>());

        Assert.Throws<KeyNotFoundException>(() => CostCalculator.CalculateUnitCost(Sheet, emptyBook, RawCosts));
    }

    [Fact]
    public void BuildInputPyramid_Raw_Material_Is_A_Leaf()
    {
        var pyramid = CostCalculator.BuildInputPyramid(Ore, 5m, Book);

        Assert.Equal(Ore, pyramid.Material);
        Assert.Equal(5m, pyramid.Quantity);
        Assert.Empty(pyramid.Inputs);
    }

    [Fact]
    public void BuildInputPyramid_Three_Level_Chain_Unfolds_Down_To_Raw_Material()
    {
        var pyramid = CostCalculator.BuildInputPyramid(Nail, 1m, Book);

        Assert.Equal(Nail, pyramid.Material);
        Assert.Equal(1m, pyramid.Quantity);

        var sheetNode = Assert.Single(pyramid.Inputs);
        Assert.Equal(Sheet, sheetNode.Material);
        Assert.Equal(0.3m, sheetNode.Quantity); // 3 sheet per 10 nail -> 0.3 sheet per nail

        var oreNode = Assert.Single(sheetNode.Inputs);
        Assert.Equal(Ore, oreNode.Material);
        Assert.Equal(0.6m, oreNode.Quantity); // 2 ore per sheet * 0.3 sheet
        Assert.Empty(oreNode.Inputs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildInputPyramid_Throws_When_Quantity_Is_Not_Positive(decimal quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CostCalculator.BuildInputPyramid(Ore, quantity, Book));
    }
}
