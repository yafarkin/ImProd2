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
    public void Construction_Throws_When_Cost_Basis_Is_Negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialOnStock(Sheet, 1m, -1m));
    }

    [Fact]
    public void Add_Increases_Quantity()
    {
        var stock = new MaterialOnStock(Sheet);

        stock.Add(10m, 0m);

        Assert.Equal(10m, stock.Quantity);
    }

    [Fact]
    public void Add_Accumulates_Cost_Basis_And_Updates_The_Average()
    {
        var stock = new MaterialOnStock(Sheet);

        stock.Add(10m, 100m); // 10 за единицу
        stock.Add(10m, 200m); // 20 за единицу

        Assert.Equal(300m, stock.TotalCostBasis);
        Assert.Equal(15m, stock.AverageUnitCost); // (100 + 200) / 20
    }

    [Fact]
    public void Remove_Decreases_Quantity()
    {
        var stock = new MaterialOnStock(Sheet, 10m);

        stock.Remove(4m);

        Assert.Equal(6m, stock.Quantity);
    }

    [Fact]
    public void Remove_Returns_The_Proportional_Cost_Basis_By_Weighted_Average()
    {
        var stock = new MaterialOnStock(Sheet, 20m, 200m); // 10 за единицу

        var removedCost = stock.Remove(5m);

        Assert.Equal(50m, removedCost); // 5 * 10
        Assert.Equal(150m, stock.TotalCostBasis); // 200 - 50
        Assert.Equal(10m, stock.AverageUnitCost); // средняя не должна поменяться
    }

    [Fact]
    public void Remove_Of_The_Entire_Stock_Zeroes_Out_The_Cost_Basis_Without_Drift()
    {
        var stock = new MaterialOnStock(Sheet, 3m, 10m); // 10 / 3 не делится нацело

        stock.Remove(3m);

        Assert.Equal(0m, stock.Quantity);
        Assert.Equal(0m, stock.TotalCostBasis);
        Assert.Equal(0m, stock.AverageUnitCost);
    }

    [Fact]
    public void AverageUnitCost_Is_Zero_When_There_Is_No_Stock()
    {
        var stock = new MaterialOnStock(Sheet);

        Assert.Equal(0m, stock.AverageUnitCost);
    }

    [Fact]
    public void Remove_Throws_When_More_Than_Available_Requested()
    {
        var stock = new MaterialOnStock(Sheet, 5m);

        Assert.Throws<InvalidOperationException>(() => stock.Remove(6m));
        Assert.Equal(5m, stock.Quantity);
    }

    [Fact]
    public void Remove_Clamps_A_Negligible_Rounding_Excess_Instead_Of_Throwing()
    {
        // Блок 7.3.3: реальный прогон на metallurgy-petrochemistry.json ловил ровно это — два
        // математически эквивалентных, но по-разному упорядоченных умножения (выход производства и
        // расход сырья на него) разошлись в последнем знаке decimal (~1e-26). Тут — тот же класс
        // расхождения на более крупном, ещё представимом decimal-литералом масштабе (5e-11).
        var stock = new MaterialOnStock(Sheet, 1m, 10m);

        var removedCost = stock.Remove(1.00000000005m);

        Assert.Equal(0m, stock.Quantity);
        Assert.Equal(0m, stock.TotalCostBasis); // защита от дрейфа на нулевом остатке
        Assert.Equal(10m, removedCost);
    }

    [Fact]
    public void Remove_Still_Throws_When_The_Excess_Is_Not_Negligible()
    {
        var stock = new MaterialOnStock(Sheet, 1m);

        Assert.Throws<InvalidOperationException>(() => stock.Remove(1.0000000002m)); // избыток заметно больше допуска округления
        Assert.Equal(1m, stock.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_Throws_When_Amount_Is_Not_Positive(decimal amount)
    {
        var stock = new MaterialOnStock(Sheet);

        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Add(amount, 0m));
    }

    [Fact]
    public void Add_Throws_When_Cost_Is_Negative()
    {
        var stock = new MaterialOnStock(Sheet);

        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Add(1m, -1m));
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
