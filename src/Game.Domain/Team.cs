namespace Game.Domain;

public sealed class Team
{
    public string Id { get; }
    public string Name { get; }
    public Sector Sector { get; }
    public Warehouse Warehouse { get; }

    private readonly List<Factory> _factories = new();
    public IReadOnlyList<Factory> Factories => _factories;

    public Team(string id, string name, Sector sector)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Team id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Team name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(sector);

        Id = id;
        Name = name;
        Sector = sector;
        Warehouse = new Warehouse();
    }

    // Sector check is enforced by the Factory constructor too; this is the entry point teams use.
    public Factory BuildFactory(string factoryId, FactoryDefinition definition, Recipe? selectedRecipe = null)
    {
        var factory = new Factory(factoryId, Sector, definition, selectedRecipe);
        _factories.Add(factory);
        return factory;
    }
}
