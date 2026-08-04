namespace Game.Domain;

/// <summary>Склад команды: остатки материалов по видам, никогда не уходящие в минус.</summary>
public sealed class Warehouse
{
    private readonly Dictionary<string, MaterialOnStock> _stock = new();

    /// <summary>Все остатки на складе, отсортированные по коду материала (без учёта порядка словаря).</summary>
    // Сортировка по коду материала: порядок перечисления словаря — не устойчивый контракт (AGENTS §2, правило 6).
    public IReadOnlyList<MaterialOnStock> Stock =>
        _stock.Values.OrderBy(stock => stock.Material.Id, StringComparer.Ordinal).ToList();

    /// <summary>Текущий остаток материала на складе; 0, если материал никогда не поступал.</summary>
    public decimal QuantityOf(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return _stock.TryGetValue(material.Id, out var stock) ? stock.Quantity : 0m;
    }

    /// <summary>Реальная средняя себестоимость единицы остатка (см. <see cref="MaterialOnStock"/>); 0, если материал никогда не поступал.</summary>
    public decimal AverageCostOf(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return _stock.TryGetValue(material.Id, out var stock) ? stock.AverageUnitCost : 0m;
    }

    /// <summary>Пополняет остаток материала на складе вместе с его реальной стоимостью (0, если поступление было бесплатным — таких на практике не бывает, но метод этого не решает за вызывающий код).</summary>
    public void Add(Material material, decimal amount, decimal cost)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (_stock.TryGetValue(material.Id, out var existing))
        {
            existing.Add(amount, cost);
        }
        else
        {
            _stock[material.Id] = new MaterialOnStock(material, amount, cost);
        }
    }

    /// <summary>Списывает материал со склада; бросает исключение при нехватке остатка. Возвращает реальную себестоимость списанной части (см. <see cref="MaterialOnStock.Remove"/>).</summary>
    public decimal Remove(Material material, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (!_stock.TryGetValue(material.Id, out var existing))
        {
            throw new InvalidOperationException($"Not enough '{material.Id}' in stock: requested {amount}, have 0.");
        }

        return existing.Remove(amount);
    }
}
