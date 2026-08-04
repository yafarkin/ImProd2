namespace Game.Domain.Tests;

public class WarehouseTests
{
    private static readonly Sector Sector = new("A", "Металлургия");
    private static readonly Material Sheet = new("sheet", "Стальные листы", Sector, level: 1);
    private static readonly Material Rebar = new("rebar", "Арматура", Sector, level: 2);

    [Fact]
    public void QuantityOf_Is_Zero_For_Material_Never_Added()
    {
        var warehouse = new Warehouse();

        Assert.Equal(0m, warehouse.QuantityOf(Sheet));
    }

    [Fact]
    public void Add_Accumulates_Quantity_For_Same_Material()
    {
        var warehouse = new Warehouse();

        warehouse.Add(Sheet, 10m, 0m);
        warehouse.Add(Sheet, 5m, 0m);

        Assert.Equal(15m, warehouse.QuantityOf(Sheet));
    }

    [Fact]
    public void Remove_Decreases_Quantity()
    {
        var warehouse = new Warehouse();
        warehouse.Add(Sheet, 10m, 0m);

        warehouse.Remove(Sheet, 4m);

        Assert.Equal(6m, warehouse.QuantityOf(Sheet));
    }

    [Fact]
    public void Remove_Throws_When_Stock_Would_Go_Negative()
    {
        var warehouse = new Warehouse();
        warehouse.Add(Sheet, 3m, 0m);

        Assert.Throws<InvalidOperationException>(() => warehouse.Remove(Sheet, 4m));
        Assert.Equal(3m, warehouse.QuantityOf(Sheet));
    }

    [Fact]
    public void Remove_Throws_When_Material_Was_Never_Stocked()
    {
        var warehouse = new Warehouse();

        Assert.Throws<InvalidOperationException>(() => warehouse.Remove(Sheet, 1m));
    }

    [Fact]
    public void Stock_Lists_All_Materials_Ordered_By_Id()
    {
        var warehouse = new Warehouse();
        warehouse.Add(Rebar, 1m, 0m);
        warehouse.Add(Sheet, 1m, 0m);

        var stock = warehouse.Stock;

        Assert.Equal(new[] { "rebar", "sheet" }, stock.Select(s => s.Material.Id));
    }

    [Fact]
    public void AverageCostOf_Is_Zero_For_Material_Never_Added()
    {
        var warehouse = new Warehouse();

        Assert.Equal(0m, warehouse.AverageCostOf(Sheet));
    }

    [Fact]
    public void AverageCostOf_Reflects_The_Weighted_Average_Of_All_Additions()
    {
        var warehouse = new Warehouse();

        warehouse.Add(Sheet, 10m, 100m); // 10 за единицу
        warehouse.Add(Sheet, 10m, 200m); // 20 за единицу

        // Средняя по всему остатку: (100 + 200) / 20 = 15.
        Assert.Equal(15m, warehouse.AverageCostOf(Sheet));
    }

    [Fact]
    public void Remove_Returns_The_Proportional_Cost_Basis_And_Leaves_The_Average_Unchanged()
    {
        var warehouse = new Warehouse();
        warehouse.Add(Sheet, 20m, 200m); // 10 за единицу

        var removedCost = warehouse.Remove(Sheet, 5m);

        Assert.Equal(50m, removedCost); // 5 * 10
        Assert.Equal(10m, warehouse.AverageCostOf(Sheet)); // средняя на остаток не меняется
    }

    [Fact]
    public void Remove_Of_The_Entire_Stock_Zeroes_Out_The_Cost_Basis_Without_Drift()
    {
        var warehouse = new Warehouse();
        warehouse.Add(Sheet, 3m, 10m); // 10 / 3 не делится нацело

        warehouse.Remove(Sheet, 3m);

        Assert.Equal(0m, warehouse.QuantityOf(Sheet));
        Assert.Equal(0m, warehouse.AverageCostOf(Sheet));
    }
}
