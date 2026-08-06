namespace Game.Config.Catalog;

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

    /// <summary>Стоимость постройки фабрики этого типа, списывается сразу (SPEC §5.6). Заглушка, требует калибровки.</summary>
    public required decimal BuildCost { get; init; }

    /// <summary>
    /// Доля от <see cref="BuildCost"/>, которую платит система при продаже/ликвидации фабрики этого
    /// типа (SPEC §5.6, §5.11), 0..1. Заглушка, требует калибровки.
    /// </summary>
    public required decimal LiquidationValueCoefficient { get; init; }

    /// <summary>
    /// Капитальные затраты на существование фабрики за ход (амортизация, охрана, аренда площадки,
    /// базовые коммунальные услуги) — списываются каждый ход, пока фабрика построена, вне
    /// зависимости от числа рабочих и объёма выпуска (запрос пользователя: «платим за фабрику, даже
    /// если она вообще не работает»). Переменная часть, растущая вместе с объёмом выпуска —
    /// отдельно, см. <see cref="Game.Config.Economy.EconomyConfig.ElectricityConsumptionPerOutputUnit"/>.
    /// Заглушка, требует калибровки.
    /// </summary>
    public required decimal FixedCostPerTurn { get; init; }
}
