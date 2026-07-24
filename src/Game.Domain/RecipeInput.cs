namespace Game.Domain;

/// <summary>
/// Один входной материал рецепта с требуемым количеством на выход, заданный рецептом.
/// </summary>
public sealed record RecipeInput
{
    /// <summary>Потребляемый материал.</summary>
    public Material Material { get; }

    /// <summary>Требуемое количество материала.</summary>
    public decimal Quantity { get; }

    public RecipeInput(Material material, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Recipe input quantity must be positive.");

        Material = material;
        Quantity = quantity;
    }
}
