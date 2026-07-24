namespace Game.Domain.Tests;

public class SectorTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Construction_Throws_When_Id_Is_Empty(string id)
    {
        Assert.Throws<ArgumentException>(() => new Sector(id, "Металлургия"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Construction_Throws_When_Name_Is_Empty(string name)
    {
        Assert.Throws<ArgumentException>(() => new Sector("A", name));
    }

    [Fact]
    public void Sectors_With_Same_Id_And_Name_Are_Equal()
    {
        var a = new Sector("A", "Металлургия");
        var b = new Sector("A", "Металлургия");

        Assert.Equal(a, b);
    }
}
