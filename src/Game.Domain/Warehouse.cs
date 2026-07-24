namespace Game.Domain;

/// <summary>Склад команды: остатки материалов по видам, никогда не уходящие в минус.</summary>
public sealed class Warehouse
{
    private readonly Dictionary<string, MaterialOnStock> _stock = new();

    /// <summary>Все остатки на складе, отсортированные по коду материала (без учёта порядка словаря).</summary>
    // Sorted by material id: dictionary enumeration order is not a stable contract (AGENTS §2 rule 6).
    public IReadOnlyList<MaterialOnStock> Stock =>
        _stock.Values.OrderBy(stock => stock.Material.Id, StringComparer.Ordinal).ToList();

    /// <summary>Текущий остаток материала на складе; 0, если материал никогда не поступал.</summary>
    public decimal QuantityOf(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return _stock.TryGetValue(material.Id, out var stock) ? stock.Quantity : 0m;
    }

    /// <summary>Пополняет остаток материала на складе.</summary>
    public void Add(Material material, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (_stock.TryGetValue(material.Id, out var existing))
        {
            existing.Add(amount);
        }
        else
        {
            _stock[material.Id] = new MaterialOnStock(material, amount);
        }
    }

    /// <summary>Списывает материал со склада; бросает исключение при нехватке остатка.</summary>
    public void Remove(Material material, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (!_stock.TryGetValue(material.Id, out var existing))
        {
            throw new InvalidOperationException($"Not enough '{material.Id}' in stock: requested {amount}, have 0.");
        }

        existing.Remove(amount);
    }
}
