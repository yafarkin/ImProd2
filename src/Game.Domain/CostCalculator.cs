namespace Game.Domain;

/// <summary>
/// Рекурсивный расчёт себестоимости материала и полной пирамиды его входов до сырья
/// (SPEC §9.3, §5.4). Чистые функции над справочником: никакого состояния, времени или рандома.
/// Базовые цены сырья и себестоимость передела — заглушки, требуют калибровки в фазе 7.
/// </summary>
public static class CostCalculator
{
    /// <summary>
    /// Себестоимость одной единицы материала: для сырья — заданная базовая цена, для передела —
    /// сумма себестоимости прямых входов, делённая на выход рецепта за цикл.
    /// </summary>
    public static decimal CalculateUnitCost(
        Material material,
        RecipeBook recipeBook,
        IReadOnlyDictionary<Material, decimal> rawMaterialCosts)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(recipeBook);
        ArgumentNullException.ThrowIfNull(rawMaterialCosts);

        if (material.IsRawMaterial)
        {
            if (!rawMaterialCosts.TryGetValue(material, out var rawCost))
            {
                throw new ArgumentException(
                    $"No base cost configured for raw material '{material.Id}'.", nameof(rawMaterialCosts));
            }

            return rawCost;
        }

        var recipe = recipeBook.GetRecipe(material);
        var inputsCost = recipe.Inputs.Sum(input =>
            input.Quantity * CalculateUnitCost(input.Material, recipeBook, rawMaterialCosts));

        return inputsCost / recipe.OutputQuantity;
    }

    /// <summary>
    /// Полная развёртка входов, нужных для получения указанного количества материала, вплоть до
    /// сырья («сколько руды в одном гвозде») — рекурсивно по всем уровням цепочки.
    /// </summary>
    public static InputPyramidNode BuildInputPyramid(Material material, decimal quantity, RecipeBook recipeBook)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(recipeBook);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be positive.");
        }

        if (material.IsRawMaterial)
        {
            return new InputPyramidNode(material, quantity, Array.Empty<InputPyramidNode>());
        }

        var recipe = recipeBook.GetRecipe(material);
        var cycles = quantity / recipe.OutputQuantity;
        var inputs = recipe.Inputs
            .Select(input => BuildInputPyramid(input.Material, input.Quantity * cycles, recipeBook))
            .ToList();

        return new InputPyramidNode(material, quantity, inputs);
    }
}
