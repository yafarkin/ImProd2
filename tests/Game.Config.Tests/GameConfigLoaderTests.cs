using Game.Config.Catalog;
using Game.Config.Loading;

namespace Game.Config.Tests;

public class GameConfigLoaderTests
{
    private static string SampleConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");

    private static string ProductionModelPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", fileName);

    private static string SessionPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", fileName);

    /// <summary>
    /// Все производственные модели, которые реально раздаёт `GameSessionHost`, должны сочетаться с
    /// любым сессионным набором и в любой комбинации проходить полную валидацию ссылочной
    /// целостности — это и есть смысл разреза модель/сессия (Block, запрос пользователя): их можно
    /// свободно комбинировать, а не только использовать в предустановленных парах. Ловит опечатки в
    /// каталоге (например, RecipeId, оставшийся от удалённого при слиянии типа фабрики), которые
    /// десериализация сама по себе не заметит.
    /// </summary>
    [Theory]
    [InlineData("standard.json", "pilot.json")]
    [InlineData("standard.json", "training.json")]
    [InlineData("standard.json", "debug.json")]
    [InlineData("debug.json", "pilot.json")]
    [InlineData("debug.json", "training.json")]
    [InlineData("debug.json", "debug.json")]
    [InlineData("metallurgy.json", "pilot.json")]
    [InlineData("metallurgy.json", "training.json")]
    [InlineData("metallurgy.json", "debug.json")]
    [InlineData("metallurgy-petrochemistry.json", "pilot.json")]
    [InlineData("metallurgy-petrochemistry.json", "training.json")]
    [InlineData("metallurgy-petrochemistry.json", "debug.json")]
    public void LoadFromFiles_Resolves_Every_Model_Session_Combination_Without_Validation_Errors(
        string productionModelFileName, string sessionFileName)
    {
        var resolved = GameConfigLoader.LoadFromFiles(
            ProductionModelPath(productionModelFileName), SessionPath(sessionFileName));

        Assert.NotEmpty(resolved.FactoryDefinitions);
    }

    /// <summary>
    /// Разрез на модель+сессию (см. <see cref="GameConfigComposer"/>) не должен терять или менять ни
    /// одного значения по сравнению со старым единым файлом: `standard.json` + `pilot.json` —
    /// это ровно тот же каталог/экономика/сессия, что и раньше был в одном `gameconfig.pilot.json`.
    /// </summary>
    [Fact]
    public void LoadFromFiles_Of_Standard_And_Pilot_Reproduces_Legacy_Combined_Sample()
    {
        var fromSplitFiles = GameConfigLoader.LoadFromFiles(ProductionModelPath("standard.json"), SessionPath("pilot.json"));
        var fromCombinedFile = GameConfigLoader.LoadFromFile(SampleConfigPath);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(fromCombinedFile.Raw),
            System.Text.Json.JsonSerializer.Serialize(fromSplitFiles.Raw));
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
