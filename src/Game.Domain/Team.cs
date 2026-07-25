namespace Game.Domain;

/// <summary>
/// Команда игроков: держит свой сектор, склад и построенные фабрики. Аналог Customer из старого
/// прототипа (см. AGENTS §5 — терминология).
/// </summary>
public sealed class Team
{
    /// <summary>Уникальный идентификатор команды, сгенерированный при её создании.</summary>
    public Ulid Id { get; }

    /// <summary>Отображаемое имя команды.</summary>
    public string Name { get; }

    /// <summary>Сектор, в котором работает команда; фабрики можно строить только этого сектора.</summary>
    public Sector Sector { get; }

    /// <summary>Склад команды.</summary>
    public Warehouse Warehouse { get; }

    private readonly List<Factory> _factories = new();

    /// <summary>Фабрики, построенные командой.</summary>
    public IReadOnlyList<Factory> Factories => _factories;

    public Team(Ulid id, string name, Sector sector)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Team id must not be empty.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Team name must not be empty.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(sector);

        Id = id;
        Name = name;
        Sector = sector;
        Warehouse = new Warehouse();
    }

    /// <summary>Строит фабрику заданного типа для команды; тип фабрики обязан быть из сектора команды.</summary>
    // Проверку сектора дублирует и конструктор Factory — это лишь точка входа для команд.
    public Factory BuildFactory(Ulid factoryId, FactoryDefinition definition, Recipe? selectedRecipe = null)
    {
        var factory = new Factory(factoryId, Sector, definition, selectedRecipe);
        _factories.Add(factory);
        return factory;
    }
}
