using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Config.Loading;

namespace Game.Config.Tests;

/// <summary>
/// Пересчёт лестницы <see cref="MaterialMarketConfig.BasePrice"/> по цепочке переделов — фикс
/// «обнаружили, что доход от сырья/железа выше, чем от честной переработки» (Блок 9.4).
/// </summary>
public class SystemSalePriceLadderCalculatorTests
{
    // rock (level0) --10--> iron (level1) --10--> iron-sheet (level2); отдельно oil (level0) --2--> plastic (level1),
    // не связанная с металлургией цепочка — проверяет, что пересчёт не трогает то, что не выбрали.
    private static ResolvedGameConfig BuildConfig()
    {
        var raw = GameConfigTestBuilder.Build(
            sectors: new[]
            {
                new SectorConfig { Id = "A", Name = "Металлургия" },
                new SectorConfig { Id = "B", Name = "Нефтехимия" },
            },
            materials: new[]
            {
                new MaterialConfig { Id = "rock", Name = "Порода", SectorId = "A", Level = 0 },
                new MaterialConfig { Id = "iron", Name = "Железо", SectorId = "A", Level = 1 },
                new MaterialConfig { Id = "iron-sheet", Name = "Листы", SectorId = "A", Level = 2 },
                new MaterialConfig { Id = "oil", Name = "Нефть", SectorId = "B", Level = 0 },
                new MaterialConfig { Id = "plastic", Name = "Пластик", SectorId = "B", Level = 1 },
            },
            recipes: new[]
            {
                new RecipeConfig { Id = "rock-mining", OutputMaterialId = "rock", OutputQuantity = 1, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1000 },
                new RecipeConfig { Id = "iron-extraction", OutputMaterialId = "iron", OutputQuantity = 1, Inputs = new[] { new RecipeInputConfig { MaterialId = "rock", Quantity = 10 } }, ProductionRate = 100 },
                new RecipeConfig { Id = "iron-sheet-from-iron", OutputMaterialId = "iron-sheet", OutputQuantity = 1, Inputs = new[] { new RecipeInputConfig { MaterialId = "iron", Quantity = 10 } }, ProductionRate = 10 },
                new RecipeConfig { Id = "oil-drilling", OutputMaterialId = "oil", OutputQuantity = 1, Inputs = Array.Empty<RecipeInputConfig>(), ProductionRate = 1 },
                new RecipeConfig { Id = "plastic-from-oil", OutputMaterialId = "plastic", OutputQuantity = 1, Inputs = new[] { new RecipeInputConfig { MaterialId = "oil", Quantity = 2 } }, ProductionRate = 1 },
            });

        raw = raw with
        {
            Economy = raw.Economy with
            {
                BaseMarketPerMaterial = new[]
                {
                    new MaterialMarketConfig { MaterialId = "rock", BasePrice = 0.02m, BaseCapacity = 5000m },
                    new MaterialMarketConfig { MaterialId = "iron", BasePrice = 15m, BaseCapacity = 500m },
                    new MaterialMarketConfig { MaterialId = "iron-sheet", BasePrice = 40m, BaseCapacity = 50m },
                    new MaterialMarketConfig { MaterialId = "oil", BasePrice = 12m, BaseCapacity = 150m },
                    new MaterialMarketConfig { MaterialId = "plastic", BasePrice = 28m, BaseCapacity = 100m },
                },
                MarginMultiplierByProcessingLevel = new[]
                {
                    new ProcessingLevelMarginConfig { Level = 1, MarginMultiplier = 1.0m },
                    new ProcessingLevelMarginConfig { Level = 2, MarginMultiplier = 1.15m },
                },
            },
        };

        return GameConfigLoader.Load(GameConfigWriter.Save(raw));
    }

    [Fact]
    public void Calculate_Grows_FullCapacityRevenue_By_GrowthPerLevel_Along_The_Chosen_Chain()
    {
        var config = BuildConfig();

        var rows = SystemSalePriceLadderCalculator.Calculate(config, growthPerLevel: 1.5m, new Dictionary<string, decimal> { ["rock"] = 0.02m });

        var rock = rows.Single(r => r.MaterialId == "rock");
        var iron = rows.Single(r => r.MaterialId == "iron");
        var ironSheet = rows.Single(r => r.MaterialId == "iron-sheet");

        Assert.True(rock.IsRepriced);
        Assert.True(iron.IsRepriced);
        Assert.True(ironSheet.IsRepriced);
        Assert.Equal(0.02m, rock.NewPrice);
        // Revenue(N) = Capacity x Price x Margin — должно расти ровно в growthPerLevel раз на каждом шаге.
        Assert.Equal(rock.NewFullCapacityRevenue * 1.5m, iron.NewFullCapacityRevenue, precision: 6);
        Assert.Equal(iron.NewFullCapacityRevenue * 1.5m, ironSheet.NewFullCapacityRevenue, precision: 6);
        // Раньше (до фикса) доход на iron был в разы больше, чем на iron-sheet — обратный эффект;
        // после фикса это больше не так ни при каком growthPerLevel > 0.
        Assert.True(ironSheet.NewFullCapacityRevenue > iron.NewFullCapacityRevenue);
    }

    [Fact]
    public void Calculate_Leaves_Chains_Without_An_Explicit_Root_Anchor_Untouched()
    {
        var config = BuildConfig();

        var rows = SystemSalePriceLadderCalculator.Calculate(config, growthPerLevel: 1.5m, new Dictionary<string, decimal> { ["rock"] = 0.02m });

        var oil = rows.Single(r => r.MaterialId == "oil");
        var plastic = rows.Single(r => r.MaterialId == "plastic");

        Assert.False(oil.IsRepriced);
        Assert.False(plastic.IsRepriced);
        Assert.Equal(12m, oil.NewPrice);
        Assert.Equal(28m, plastic.NewPrice);
    }

    [Fact]
    public void Calculate_Reproduces_The_Reported_Debug_Config_Numbers()
    {
        // Числа, которыми диагностировали баг-репорт пользователя (Блок 9.4) — если формула когда-то
        // случайно сломается, этот тест первым перестанет совпадать с уже согласованными цифрами.
        var config = BuildConfig();

        var rows = SystemSalePriceLadderCalculator.Calculate(config, growthPerLevel: 1.5m, new Dictionary<string, decimal> { ["rock"] = 0.02m });

        var iron = rows.Single(r => r.MaterialId == "iron");
        var ironSheet = rows.Single(r => r.MaterialId == "iron-sheet");
        Assert.Equal(0.3m, iron.NewPrice, precision: 3);
        Assert.Equal(3.913m, ironSheet.NewPrice, precision: 3);
    }

    [Fact]
    public void Calculate_Throws_For_A_Non_Positive_GrowthPerLevel()
    {
        var config = BuildConfig();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SystemSalePriceLadderCalculator.Calculate(config, growthPerLevel: 0m, new Dictionary<string, decimal>()));
    }

    [Fact]
    public void Apply_Updates_Only_Repriced_Materials_And_Produces_A_Config_That_Still_Loads()
    {
        var config = BuildConfig();
        var rows = SystemSalePriceLadderCalculator.Calculate(config, growthPerLevel: 1.5m, new Dictionary<string, decimal> { ["rock"] = 0.02m });

        var updated = SystemSalePriceLadderCalculator.Apply(config.Raw, rows);
        var reloaded = GameConfigLoader.Load(GameConfigWriter.Save(updated));

        Assert.Equal(0.3m, reloaded.Raw.Economy.BaseMarketPerMaterial.Single(m => m.MaterialId == "iron").BasePrice, precision: 3);
        // Цепочка нефти цену не поменяла — её не выбрали корнем.
        Assert.Equal(12m, reloaded.Raw.Economy.BaseMarketPerMaterial.Single(m => m.MaterialId == "oil").BasePrice);
        // Ёмкость и маржа не тронуты — фикс только про цену.
        Assert.Equal(500m, reloaded.Raw.Economy.BaseMarketPerMaterial.Single(m => m.MaterialId == "iron").BaseCapacity);
    }
}
