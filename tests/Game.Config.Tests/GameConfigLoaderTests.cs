using Game.Config.Catalog;
using Game.Config.Loading;

namespace Game.Config.Tests;

public class GameConfigLoaderTests
{
    private static string SampleConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");

    /// <summary>
    /// Все три конфига, которые реально раздаёт `GameSessionHost` (админка «Полный»/«Отладочный»/
    /// «Тренировочный»), должны проходить полную валидацию ссылочной целостности — не только pilot,
    /// который проверяют остальные тесты этого файла подробно. Ловит опечатки в каталоге (например,
    /// RecipeId, оставшийся от удалённого при слиянии типа фабрики), которые деsериализация сама по
    /// себе не заметит.
    /// </summary>
    [Theory]
    [InlineData("gameconfig.pilot.json")]
    [InlineData("gameconfig.debug.json")]
    [InlineData("gameconfig.training.json")]
    public void LoadFromFile_Resolves_Every_Deployed_Sample_Config_Without_Validation_Errors(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", fileName);

        var resolved = GameConfigLoader.LoadFromFile(path);

        Assert.NotEmpty(resolved.FactoryDefinitions);
    }

    [Fact]
    public void LoadFromFile_Resolves_Sample_Config_Into_Domain_Graph()
    {
        var resolved = GameConfigLoader.LoadFromFile(SampleConfigPath);

        Assert.Equal(2, resolved.Sectors.Count);
        Assert.Equal(5, resolved.Materials.Count);
        Assert.Equal(5, resolved.FactoryDefinitions.Count);

        var rebar = resolved.Materials["rebar"];
        var rebarRecipe = resolved.RecipeBook.GetRecipe(rebar);
        Assert.Equal("rebar-from-sheet", rebarRecipe.Id);
        Assert.Same(resolved.Materials["sheet"], rebarRecipe.Inputs[0].Material);

        var ore = resolved.Materials["ore"];
        var oreRecipe = resolved.RecipeBook.GetRecipe(ore);
        Assert.Equal("ore-mining", oreRecipe.Id);
        Assert.Empty(oreRecipe.Inputs); // сырьё добывается, а не строится из других материалов

        var steelMill = Assert.Single(resolved.FactoryDefinitions, factory => factory.Id == "steel-mill");
        Assert.Same(resolved.Materials["ore"].Sector, steelMill.Sector);
    }

    [Fact]
    public void Load_Throws_With_Clear_Message_When_Json_Is_Malformed()
    {
        var exception = Assert.Throws<GameConfigValidationException>(() => GameConfigLoader.Load("{ this is not json"));

        Assert.Contains(exception.Errors, error => error.Contains("malformed"));
    }

    [Fact]
    public void Load_Throws_With_Clear_Message_When_Required_Section_Is_Missing()
    {
        // "Sectors" (как и всё остальное) — required-свойство GameConfig.
        var exception = Assert.Throws<GameConfigValidationException>(() => GameConfigLoader.Load("{}"));

        Assert.NotEmpty(exception.Errors);
    }

    [Fact]
    public void Load_Throws_With_All_Referential_Integrity_Problems_When_Config_Is_Broken()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { new SectorConfig { Id = "A", Name = "Металлургия" } },
            materials: new[] { new MaterialConfig { Id = "sheet", Name = "Лист", SectorId = "missing-sector", Level = 1 } });
        var json = System.Text.Json.JsonSerializer.Serialize(config);

        var exception = Assert.Throws<GameConfigValidationException>(() => GameConfigLoader.Load(json));

        Assert.Contains(exception.Errors, error => error.Contains("unknown sector 'missing-sector'"));
        Assert.Contains("problem(s)", exception.Message);
    }
}
