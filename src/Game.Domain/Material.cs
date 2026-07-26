namespace Game.Domain;

/// <summary>
/// Единица продукции: хранится на складе, продаётся, поставляется по контракту.
/// Неизменяемый справочный объект, привязанный к сектору-владельцу.
/// </summary>
public sealed record Material
{
    /// <summary>Уникальный код материала.</summary>
    public string Id { get; }

    /// <summary>Отображаемое имя материала.</summary>
    public string Name { get; }

    /// <summary>Сектор, к которому принадлежит материал.</summary>
    public Sector Sector { get; }

    /// <summary>Уровень передела: 0 — сырьё, добываемое собственной фабрикой команды; выше — продукт переработки.</summary>
    public int Level { get; }

    public Material(string id, string name, Sector sector, int level)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Material id must not be empty.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Material name must not be empty.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(sector);
        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Material level must not be negative.");
        }

        Id = id;
        Name = name;
        Sector = sector;
        Level = level;
    }

    /// <summary>
    /// Материалы уровня 0 добываются фабрикой-добытчиком (шахта, скважина, плантация — рецепт без
    /// входов, только рабочие) — как и любой другой материал, а не покупаются извне.
    /// </summary>
    public bool IsRawMaterial => Level == 0;
}
