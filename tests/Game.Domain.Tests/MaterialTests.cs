namespace Game.Domain.Tests;

public class MaterialTests
{
    private static readonly Sector Sector = new("A", "Металлургия");

    [Fact]
    public void Construction_Throws_When_Level_Is_Negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material("ore", "Руда", Sector, -1));
    }

    [Fact]
    public void IsRawMaterial_True_Only_At_Level_Zero()
    {
        var ore = new Material("ore", "Железная руда", Sector, level: 0);
        var sheet = new Material("sheet", "Стальные листы", Sector, level: 1);

        Assert.True(ore.IsRawMaterial);
        Assert.False(sheet.IsRawMaterial);
    }
}
