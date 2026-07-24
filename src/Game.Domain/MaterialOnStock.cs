namespace Game.Domain;

/// <summary>Остаток одного материала на складе команды.</summary>
public sealed class MaterialOnStock
{
    /// <summary>Материал, для которого учитывается остаток.</summary>
    public Material Material { get; }

    /// <summary>Текущее количество на складе; никогда не отрицательно.</summary>
    public decimal Quantity { get; private set; }

    public MaterialOnStock(Material material, decimal quantity = 0m)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Stock quantity must not be negative.");
        }

        Material = material;
        Quantity = quantity;
    }

    /// <summary>Увеличивает остаток на положительное количество.</summary>
    public void Add(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
        }

        Quantity += amount;
    }

    /// <summary>Уменьшает остаток; бросает исключение, если запрошено больше, чем есть в наличии.</summary>
    public void Remove(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");
        }
        if (amount > Quantity)
        {
            throw new InvalidOperationException(
                $"Not enough '{Material.Id}' in stock: requested {amount}, have {Quantity}.");
        }

        Quantity -= amount;
    }
}
