namespace Game.Config.Tests;

public class GameConfigLoaderTests
{
    private static string SampleConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");

    [Fact]
    public void LoadFromFile_Resolves_Sample_Config_Into_Domain_Graph()
    {
        var resolved = GameConfigLoader.LoadFromFile(SampleConfigPath);

        Assert.Equal(2, resolved.Sectors.Count);
        Assert.Equal(5, resolved.Materials.Count);
        Assert.Equal(3, resolved.FactoryDefinitions.Count);

        var rebar = resolved.Materials["rebar"];
        var rebarRecipe = resolved.RecipeBook.GetRecipe(rebar);
        Assert.Equal("rebar-from-sheet", rebarRecipe.Id);
        Assert.Same(resolved.Materials["sheet"], rebarRecipe.Inputs[0].Material);

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
        // "Sectors" (and everything else) is a required member of GameConfig.
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
