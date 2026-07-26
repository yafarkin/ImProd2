using System.Text.Json;
using Game.Config.Catalog;
using Game.Config.Loading;

namespace Game.Config.Tests;

public class GameConfigWriterTests
{
    [Fact]
    public void Create_Save_And_Restore_A_Config_Round_Trips_Losslessly()
    {
        var sectorA = new SectorConfig { Id = "A", Name = "Металлургия" };
        var ore = new MaterialConfig { Id = "ore", Name = "Руда", SectorId = "A", Level = 0 };
        var sheet = new MaterialConfig { Id = "sheet", Name = "Лист", SectorId = "A", Level = 1 };
        var oreMining = new RecipeConfig
        {
            Id = "ore-mining",
            OutputMaterialId = "ore",
            OutputQuantity = 1m,
            Inputs = Array.Empty<RecipeInputConfig>(),
            ProductionRate = 1m,
        };
        var sheetFromOre = new RecipeConfig
        {
            Id = "sheet-from-ore",
            OutputMaterialId = "sheet",
            OutputQuantity = 1m,
            Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 2m } },
            ProductionRate = 1m,
        };
        var mine = new FactoryDefinitionConfig
        {
            Id = "mine",
            Name = "Рудник",
            SectorId = "A",
            RecipeIds = new[] { "ore-mining" },
            BuildCost = 100m,
            LiquidationValueCoefficient = 0.5m,
        };
        var steelMill = new FactoryDefinitionConfig
        {
            Id = "steel-mill",
            Name = "Сталелитейный завод",
            SectorId = "A",
            RecipeIds = new[] { "sheet-from-ore" },
            BuildCost = 100m,
            LiquidationValueCoefficient = 0.5m,
        };

        var original = GameConfigTestBuilder.Build(
            sectors: new[] { sectorA },
            materials: new[] { ore, sheet },
            recipes: new[] { oreMining, sheetFromOre },
            factoryDefinitions: new[] { mine, steelMill });

        var path = Path.Combine(Path.GetTempPath(), $"gameconfig-roundtrip-{Guid.NewGuid():N}.json");
        try
        {
            GameConfigWriter.SaveToFile(original, path);
            Assert.True(File.Exists(path));

            var restored = GameConfigLoader.LoadFromFile(path);

            // GameConfig — record, но списковые свойства ломают его равенство (List<T> не
            // переопределяет Equals, поэтому сравнение уходит в проверку по ссылке). Сравнение
            // заново сериализованного JSON с обеих сторон и доказывает, что каждое поле пережило
            // round trip, а не просто что верхнеуровневые ссылки различаются.
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(restored.Raw));

            // И доказываем, что восстановленный конфиг реально рабочий, а не только текстово идентичен.
            Assert.Equal("Металлургия", restored.Sectors.Single().Name);
            var sheetMaterial = restored.Materials["sheet"];
            var recipe = restored.RecipeBook.GetRecipe(sheetMaterial);
            Assert.Equal("sheet-from-ore", recipe.Id);
            Assert.Equal("ore", recipe.Inputs.Single().Material.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
