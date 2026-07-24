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

        warehouse.Add(Sheet, 10m);
        warehouse.Add(Sheet, 5m);

        Assert.Equal(15m, warehouse.QuantityOf(Sheet));
    }

    [Fact]
    public void Remove_Decreases_Quantity()
    {
        var warehouse = new Warehouse();
        warehouse.Add(Sheet, 10m);

        warehouse.Remove(Sheet, 4m);

        Assert.Equal(6m, warehouse.QuantityOf(Sheet));
    }

    [Fact]
    public void Remove_Throws_When_Stock_Would_Go_Negative()
    {
        var warehouse = new Warehouse();
        warehouse.Add(Sheet, 3m);

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
        warehouse.Add(Rebar, 1m);
        warehouse.Add(Sheet, 1m);

        var stock = warehouse.Stock;

        Assert.Equal(new[] { "rebar", "sheet" }, stock.Select(s => s.Material.Id));
    }
}
