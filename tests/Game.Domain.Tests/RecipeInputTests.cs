namespace Game.Domain.Tests;

public class RecipeInputTests
{
    private static readonly Sector Sector = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", Sector, level: 0);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construction_Throws_When_Quantity_Is_Not_Positive(decimal quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecipeInput(Ore, quantity));
    }
}
