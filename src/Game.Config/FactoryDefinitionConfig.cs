namespace Game.Config;

/// <summary>Тип фабрики, как он задан в GameConfig: свой сектор и список рецептов, которые может выпускать.</summary>
public sealed record FactoryDefinitionConfig
{
    /// <summary>Уникальный код типа фабрики.</summary>
    public required string Id { get; init; }

    /// <summary>Отображаемое имя типа фабрики.</summary>
    public required string Name { get; init; }

    /// <summary>Код сектора, которому принадлежит фабрика (<see cref="SectorConfig.Id"/>).</summary>
    public required string SectorId { get; init; }

    /// <summary>Коды рецептов, доступных фабрике этого типа (<see cref="RecipeConfig.Id"/>).</summary>
    public required IReadOnlyList<string> RecipeIds { get; init; }
}
