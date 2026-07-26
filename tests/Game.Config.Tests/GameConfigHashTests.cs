using Game.Config.Catalog;
using Game.Config.Loading;

namespace Game.Config.Tests;

public class GameConfigHashTests
{
    [Fact]
    public void Same_Config_Content_Yields_The_Same_Hash()
    {
        var a = GameConfigTestBuilder.Build();
        var b = GameConfigTestBuilder.Build();

        Assert.Equal(GameConfigHash.Compute(a), GameConfigHash.Compute(b));
    }

    [Fact]
    public void A_Changed_Value_Yields_A_Different_Hash()
    {
        var baseline = GameConfigTestBuilder.Build();
        var changed = GameConfigTestBuilder.Build(
            sectors: new[] { new SectorConfig { Id = "A", Name = "Переименованный сектор" } });

        Assert.NotEqual(GameConfigHash.Compute(baseline), GameConfigHash.Compute(changed));
    }
}
