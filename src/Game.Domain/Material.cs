namespace Game.Domain;

public sealed record Material
{
    public string Id { get; }
    public string Name { get; }
    public Sector Sector { get; }
    public int Level { get; }

    public Material(string id, string name, Sector sector, int level)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Material id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Material name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(sector);
        if (level < 0)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Material level must not be negative.");

        Id = id;
        Name = name;
        Sector = sector;
        Level = level;
    }

    // Level 0 materials are bought directly from the system, not produced via a Recipe.
    public bool IsRawMaterial => Level == 0;
}
