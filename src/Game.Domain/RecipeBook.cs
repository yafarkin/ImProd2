namespace Game.Domain;

/// <summary>
/// Справочник «какой рецепт производит какой материал». Нужен для рекурсивного разложения
/// пирамиды входов и себестоимости, поскольку сам <see cref="Recipe"/> знает только свои прямые
/// входы, а не то, как произведён каждый из них.
/// </summary>
public sealed class RecipeBook
{
    private readonly Dictionary<Material, Recipe> _recipesByOutput;

    public RecipeBook(IEnumerable<Recipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);

        _recipesByOutput = new Dictionary<Material, Recipe>();
        foreach (var recipe in recipes)
        {
            if (!_recipesByOutput.TryAdd(recipe.Output, recipe))
            {
                throw new ArgumentException(
                    $"Multiple recipes produce material '{recipe.Output.Id}'.", nameof(recipes));
            }
        }
    }

    /// <summary>Возвращает рецепт, производящий материал, или бросает, если его нет в справочнике.</summary>
    public Recipe GetRecipe(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!_recipesByOutput.TryGetValue(material, out var recipe))
        {
            throw new KeyNotFoundException($"No recipe produces material '{material.Id}'.");
        }

        return recipe;
    }

    /// <summary>Возвращает рецепт, производящий материал, или null, если его нет в справочнике.</summary>
    public Recipe? TryGetRecipe(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return _recipesByOutput.GetValueOrDefault(material);
    }
}
