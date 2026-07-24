namespace Game.Domain;

/// <summary>
/// Отрасль экономики (металлургия, нефтегазохимия, лес/агротекстиль, электроника).
/// Неизменяемый справочный объект: часть графа конфигурации, сравнивается по значению.
/// </summary>
public sealed record Sector
{
    /// <summary>Уникальный код сектора.</summary>
    public string Id { get; }

    /// <summary>Отображаемое имя сектора.</summary>
    public string Name { get; }

    public Sector(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Sector id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sector name must not be empty.", nameof(name));

        Id = id;
        Name = name;
    }
}
