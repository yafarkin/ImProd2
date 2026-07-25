using Game.Config.Catalog;
using Game.Domain;

namespace Game.Config.Loading;

/// <summary>
/// Строит объектный граф Game.Domain из уже провалидированного GameConfig. Не проверяет
/// целостность сама — это ответственность <see cref="GameConfigValidator"/>, вызываемого раньше;
/// здесь любая рассинхронизация проявится как необработанное исключение из конструктора домена.
/// </summary>
internal static class GameConfigResolver
{
    public static ResolvedGameConfig Resolve(GameConfig config)
    {
        var sectors = config.Sectors.ToDictionary(
            sector => sector.Id,
            sector => new Sector(sector.Id, sector.Name));

        var materials = config.Materials.ToDictionary(
            material => material.Id,
            material => new Material(material.Id, material.Name, sectors[material.SectorId], material.Level));

        var recipes = new Dictionary<string, Recipe>();
        foreach (var recipeConfig in config.Recipes)
        {
            var inputs = recipeConfig.Inputs
                .Select(input => new RecipeInput(materials[input.MaterialId], input.Quantity))
                .ToList();

            recipes[recipeConfig.Id] = new Recipe(
                recipeConfig.Id,
                materials[recipeConfig.OutputMaterialId],
                recipeConfig.OutputQuantity,
                inputs,
                recipeConfig.ProductionRate);
        }

        var factoryDefinitions = config.FactoryDefinitions
            .Select(factoryConfig => new FactoryDefinition(
                factoryConfig.Id,
                factoryConfig.Name,
                sectors[factoryConfig.SectorId],
                factoryConfig.RecipeIds.Select(recipeId => recipes[recipeId]).ToList()))
            .ToList();

        return new ResolvedGameConfig(
            config,
            sectors.Values.ToList(),
            materials,
            new RecipeBook(recipes.Values),
            factoryDefinitions);
    }
}
