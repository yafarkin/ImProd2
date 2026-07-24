namespace Game.Domain;

public sealed record RecipeInput
{
    public Material Material { get; }
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
