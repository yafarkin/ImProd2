namespace Game.Domain.Tests;

public class MaterialOnStockTests
{
    private static readonly Sector Sector = new("A", "Металлургия");
    private static readonly Material Sheet = new("sheet", "Стальные листы", Sector, level: 1);

    [Fact]
    public void Construction_Throws_When_Quantity_Is_Negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialOnStock(Sheet, -1m));
    }

    [Fact]
    public void Add_Increases_Quantity()
    {
        var stock = new MaterialOnStock(Sheet);

        stock.Add(10m);

        Assert.Equal(10m, stock.Quantity);
    }

    [Fact]
    public void Remove_Decreases_Quantity()
    {
        var stock = new MaterialOnStock(Sheet, 10m);

        stock.Remove(4m);

        Assert.Equal(6m, stock.Quantity);
    }

    [Fact]
    public void Remove_Throws_When_More_Than_Available_Requested()
    {
        var stock = new MaterialOnStock(Sheet, 5m);

        Assert.Throws<InvalidOperationException>(() => stock.Remove(6m));
        Assert.Equal(5m, stock.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_Throws_When_Amount_Is_Not_Positive(decimal amount)
    {
        var stock = new MaterialOnStock(Sheet);

        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Add(amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Remove_Throws_When_Amount_Is_Not_Positive(decimal amount)
    {
        var stock = new MaterialOnStock(Sheet, 10m);

        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Remove(amount));
    }
}
