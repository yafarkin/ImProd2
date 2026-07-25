namespace Game.Config;

/// <summary>
/// Правило преобразования, как оно задано в GameConfig: набор входов (по коду материала) даёт
/// заданное количество выходного материала. Требует ссылочной проверки при загрузке (Блок 2.2).
/// </summary>
public sealed record RecipeConfig
{
    /// <summary>Уникальный код рецепта.</summary>
    public required string Id { get; init; }

    /// <summary>Код производимого материала (<see cref="MaterialConfig.Id"/>).</summary>
    public required string OutputMaterialId { get; init; }

    /// <summary>Количество выходного материала за один цикл производства.</summary>
    public required decimal OutputQuantity { get; init; }

    /// <summary>Прямые входы рецепта.</summary>
    public required IReadOnlyList<RecipeInputConfig> Inputs { get; init; }

    /// <summary>Скорость производства (единиц выхода за такт на единицу мощности); требует калибровки.</summary>
    public required decimal ProductionRate { get; init; }
}
