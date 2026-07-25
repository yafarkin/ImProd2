namespace Game.Config;

/// <summary>Материал справочника, как он задан в GameConfig. Ссылается на сектор по коду.</summary>
public sealed record MaterialConfig
{
    /// <summary>Уникальный код материала.</summary>
    public required string Id { get; init; }

    /// <summary>Отображаемое имя материала.</summary>
    public required string Name { get; init; }

    /// <summary>Код сектора, к которому принадлежит материал (<see cref="SectorConfig.Id"/>).</summary>
    public required string SectorId { get; init; }

    /// <summary>Уровень передела; 0 — сырьё, закупаемое у системы.</summary>
    public required int Level { get; init; }
}
