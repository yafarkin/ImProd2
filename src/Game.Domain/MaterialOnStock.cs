namespace Game.Domain;

public sealed class MaterialOnStock
{
    public Material Material { get; }
    public decimal Quantity { get; private set; }

    public MaterialOnStock(Material material, decimal quantity = 0m)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Stock quantity must not be negative.");

        Material = material;
        Quantity = quantity;
    }

    public void Add(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");

        Quantity += amount;
    }

    public void Remove(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
        if (amount > Quantity)
            throw new InvalidOperationException(
                $"Not enough '{Material.Id}' in stock: requested {amount}, have {Quantity}.");

        Quantity -= amount;
    }
}
