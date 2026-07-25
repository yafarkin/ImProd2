namespace Game.Domain;

/// <summary>
/// Узел пирамиды входов: сколько материала нужно и из чего он, в свою очередь, произведён.
/// У сырья (<see cref="Material.IsRawMaterial"/>) список входов пуст — это основание пирамиды.
/// </summary>
public sealed class InputPyramidNode
{
    /// <summary>Материал этого узла пирамиды.</summary>
    public Material Material { get; }

    /// <summary>Требуемое количество материала на этом уровне разложения.</summary>
    public decimal Quantity { get; }

    /// <summary>Прямые входы, из которых произведено это количество материала; пусто для сырья.</summary>
    public IReadOnlyList<InputPyramidNode> Inputs { get; }

    public InputPyramidNode(Material material, decimal quantity, IReadOnlyList<InputPyramidNode> inputs)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Pyramid node quantity must be positive.");
        }
        ArgumentNullException.ThrowIfNull(inputs);

        Material = material;
        Quantity = quantity;
        Inputs = inputs;
    }
}
