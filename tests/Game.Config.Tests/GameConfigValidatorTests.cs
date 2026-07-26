using Game.Config.Catalog;
using Game.Config.Loading;

namespace Game.Config.Tests;

public class GameConfigValidatorTests
{
    private static readonly SectorConfig SectorA = new() { Id = "A", Name = "Металлургия" };
    private static readonly SectorConfig SectorB = new() { Id = "B", Name = "Нефтегазохимия" };
    private static readonly MaterialConfig Ore = new() { Id = "ore", Name = "Руда", SectorId = "A", Level = 0 };
    private static readonly MaterialConfig Sheet = new() { Id = "sheet", Name = "Лист", SectorId = "A", Level = 1 };
    private static readonly MaterialConfig Rebar = new() { Id = "rebar", Name = "Арматура", SectorId = "A", Level = 2 };

    private static RecipeConfig OreMining() => new()
    {
        Id = "ore-mining",
        OutputMaterialId = "ore",
        OutputQuantity = 1m,
        Inputs = Array.Empty<RecipeInputConfig>(),
        ProductionRate = 1m,
    };

    private static RecipeConfig SheetFromOre() => new()
    {
        Id = "sheet-from-ore",
        OutputMaterialId = "sheet",
        OutputQuantity = 1m,
        Inputs = new[] { new RecipeInputConfig { MaterialId = "ore", Quantity = 2m } },
        ProductionRate = 1m,
    };

    private static RecipeConfig RebarFromSheet() => new()
    {
        Id = "rebar-from-sheet",
        OutputMaterialId = "rebar",
        OutputQuantity = 10m,
        Inputs = new[] { new RecipeInputConfig { MaterialId = "sheet", Quantity = 3m } },
        ProductionRate = 2m,
    };

    [Fact]
    public void Valid_Config_Has_No_Errors()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Ore, Sheet, Rebar },
            recipes: new[] { OreMining(), SheetFromOre(), RebarFromSheet() },
            factoryDefinitions: new[]
            {
                new FactoryDefinitionConfig { Id = "mine", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" } },
                new FactoryDefinitionConfig { Id = "steel-mill", Name = "Завод", SectorId = "A", RecipeIds = new[] { "sheet-from-ore" } },
                new FactoryDefinitionConfig { Id = "rolling-mill", Name = "Стан", SectorId = "A", RecipeIds = new[] { "rebar-from-sheet" } },
            });

        var errors = GameConfigValidator.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void Material_Referencing_Unknown_Sector_Is_Reported()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Ore with { SectorId = "Z" } });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("unknown sector 'Z'"));
    }

    [Fact]
    public void Recipe_Referencing_Unknown_Output_Material_Is_Reported()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Ore },
            recipes: new[] { SheetFromOre() });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("unknown material 'sheet'"));
    }

    [Fact]
    public void Recipe_Consuming_Unknown_Input_Material_Is_Reported()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Sheet },
            recipes: new[] { SheetFromOre() });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("unknown material 'ore'"));
    }

    [Fact]
    public void Duplicate_Material_Id_Is_Reported()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Ore, Ore });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("Duplicate Material id 'ore'"));
    }

    [Fact]
    public void Recipe_Producing_Raw_Material_With_Inputs_Is_Reported()
    {
        var otherOre = new MaterialConfig { Id = "ore2", Name = "Другая руда", SectorId = "A", Level = 0 };
        var badRecipe = new RecipeConfig
        {
            Id = "bad",
            OutputMaterialId = "ore",
            OutputQuantity = 1m,
            Inputs = new[] { new RecipeInputConfig { MaterialId = "ore2", Quantity = 1m } },
            ProductionRate = 1m,
        };
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Ore, otherOre },
            recipes: new[] { badRecipe, OreMining() with { Id = "ore2-mining", OutputMaterialId = "ore2" } });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("raw material 'ore'"));
    }

    [Fact]
    public void Recipe_Producing_Raw_Material_Without_Inputs_Is_Valid()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Ore },
            recipes: new[] { OreMining() },
            factoryDefinitions: new[]
            {
                new FactoryDefinitionConfig { Id = "mine", Name = "Рудник", SectorId = "A", RecipeIds = new[] { "ore-mining" } },
            });

        var errors = GameConfigValidator.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void Material_Without_Any_Recipe_Is_Reported_As_Unreachable_Raw_Or_Not()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Ore, Sheet });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("Material 'sheet'") && error.Contains("unreachable"));
        Assert.Contains(errors, error => error.Contains("Material 'ore'") && error.Contains("unreachable"));
    }

    [Fact]
    public void Material_Produced_By_Two_Recipes_Is_Reported()
    {
        var secondSheetRecipe = SheetFromOre() with { Id = "sheet-from-ore-2" };
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { Ore, Sheet },
            recipes: new[] { SheetFromOre(), secondSheetRecipe });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("Material 'sheet' is produced by multiple recipes"));
    }

    [Fact]
    public void FactoryDefinition_Offering_Recipe_From_Another_Sector_Is_Reported()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA, SectorB },
            materials: new[] { Ore, Sheet },
            recipes: new[] { SheetFromOre() },
            factoryDefinitions: new[]
            {
                new FactoryDefinitionConfig { Id = "wrong-mill", Name = "Не тот завод", SectorId = "B", RecipeIds = new[] { "sheet-from-ore" } },
            });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("wrong-mill") && error.Contains("sector 'A'"));
    }

    [Fact]
    public void FactoryDefinition_Referencing_Unknown_Recipe_Is_Reported()
    {
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            factoryDefinitions: new[]
            {
                new FactoryDefinitionConfig { Id = "steel-mill", Name = "Завод", SectorId = "A", RecipeIds = new[] { "no-such-recipe" } },
            });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("unknown recipe 'no-such-recipe'"));
    }

    [Fact]
    public void Circular_Production_Dependency_Is_Reported()
    {
        var x = new MaterialConfig { Id = "x", Name = "X", SectorId = "A", Level = 1 };
        var y = new MaterialConfig { Id = "y", Name = "Y", SectorId = "A", Level = 1 };
        var xFromY = new RecipeConfig
        {
            Id = "x-from-y",
            OutputMaterialId = "x",
            OutputQuantity = 1m,
            Inputs = new[] { new RecipeInputConfig { MaterialId = "y", Quantity = 1m } },
            ProductionRate = 1m,
        };
        var yFromX = new RecipeConfig
        {
            Id = "y-from-x",
            OutputMaterialId = "y",
            OutputQuantity = 1m,
            Inputs = new[] { new RecipeInputConfig { MaterialId = "x", Quantity = 1m } },
            ProductionRate = 1m,
        };
        var config = GameConfigTestBuilder.Build(
            sectors: new[] { SectorA },
            materials: new[] { x, y },
            recipes: new[] { xFromY, yFromX });

        var errors = GameConfigValidator.Validate(config);

        Assert.Contains(errors, error => error.Contains("Circular production dependency"));
    }
}
