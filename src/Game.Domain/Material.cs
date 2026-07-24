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

    /// <summary>Уровень передела: 0 — сырьё, покупаемое у системы; выше — продукт переработки.</summary>
    public int Level { get; }

    public Material(string id, string name, Sector sector, int level)
    {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Material id must not be empty.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Material name must not be empty.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(sector);
        if (level < 0) {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Material level must not be negative.");
        }

        Id = id;
        Name = name;
        Sector = sector;
        Level = level;
    }

    /// <summary>Level 0 materials are bought directly from the system, not produced via a Recipe.</summary>
    public bool IsRawMaterial => Level == 0;
}
