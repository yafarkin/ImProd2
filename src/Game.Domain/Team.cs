namespace Game.Domain;

/// <summary>
/// Команда игроков: держит свой сектор, склад и построенные фабрики. Аналог Customer из старого
/// прототипа (см. AGENTS §5 — терминология).
/// </summary>
public sealed class Team
{
    /// <summary>Уникальный код команды.</summary>
    public string Id { get; }

    /// <summary>Отображаемое имя команды.</summary>
    public string Name { get; }

    /// <summary>Сектор, в котором работает команда; фабрики можно строить только этого сектора.</summary>
    public Sector Sector { get; }

    /// <summary>Склад команды.</summary>
    public Warehouse Warehouse { get; }

    private readonly List<Factory> _factories = new();

    /// <summary>Фабрики, построенные командой.</summary>
    public IReadOnlyList<Factory> Factories => _factories;

    public Team(string id, string name, Sector sector)
    {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Team id must not be empty.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Team name must not be empty.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(sector);

        Id = id;
        Name = name;
        Sector = sector;
        Warehouse = new Warehouse();
    }

    /// <summary>Строит фабрику заданного типа для команды; тип фабрики обязан быть из сектора команды.</summary>
    // Sector check is enforced by the Factory constructor too; this is the entry point teams use.
    public Factory BuildFactory(string factoryId, FactoryDefinition definition, Recipe? selectedRecipe = null)
    {
        var factory = new Factory(factoryId, Sector, definition, selectedRecipe);
        _factories.Add(factory);
        return factory;
    }
}
