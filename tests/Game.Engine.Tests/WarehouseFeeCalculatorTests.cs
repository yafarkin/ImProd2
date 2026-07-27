using Game.Config.Economy;

namespace Game.Engine.Tests;

/// <summary>Плата за превышение бесплатного лимита склада (Блок 9.2, SPEC §5.7).</summary>
public class WarehouseFeeCalculatorTests
{
    private static readonly WarehouseConfig Config = new() { FreeCapacity = 10m, OverageFeePerUnit = 2m };

    [Fact]
    public void Calculate_Returns_Zero_When_Stock_Is_Within_Free_Capacity()
    {
        var result = WarehouseFeeCalculator.Calculate(10m, Config);

        Assert.Equal(0m, result.OverageQuantity);
        Assert.Equal(0m, result.Fee);
    }

    [Fact]
    public void Calculate_Charges_The_Configured_Rate_Per_Unit_Over_The_Free_Capacity()
    {
        var result = WarehouseFeeCalculator.Calculate(15m, Config);

        Assert.Equal(5m, result.OverageQuantity);
        Assert.Equal(10m, result.Fee);
    }

    [Fact]
    public void Calculate_Throws_For_A_Null_Config()
    {
        Assert.Throws<ArgumentNullException>(() => WarehouseFeeCalculator.Calculate(15m, null!));
    }
}
