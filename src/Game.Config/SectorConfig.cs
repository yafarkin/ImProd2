namespace Game.Config;

/// <summary>Отрасль экономики, как она задана в GameConfig.</summary>
public sealed record SectorConfig
{
    /// <summary>Уникальный код сектора.</summary>
    public required string Id { get; init; }

    /// <summary>Отображаемое имя сектора.</summary>
    public required string Name { get; init; }
}
