namespace Game.Domain;

/// <summary>
/// Правило преобразования: набор входных материалов даёт заданное количество выходного материала.
/// Часть графа конфигурации; сравнивается по ссылке (одна и та же сессия использует один
/// канонический экземпляр рецепта).
/// </summary>
public sealed class Recipe
{
    /// <summary>Уникальный код рецепта.</summary>
    public string Id { get; }

    /// <summary>Производимый материал.</summary>
    public Material Output { get; }

    /// <summary>Количество выходного материала за один цикл производства.</summary>
    public decimal OutputQuantity { get; }

    /// <summary>Прямые входы рецепта (без рекурсивного разложения до сырья).</summary>
    public IReadOnlyList<RecipeInput> Inputs { get; }

    /// <summary>Скорость производства (единиц выхода за такт на единицу мощности); требует калибровки.</summary>
    public decimal ProductionRate { get; }

    public Recipe(
        string id,
        Material output,
        decimal outputQuantity,
        IReadOnlyList<RecipeInput> inputs,
        decimal productionRate)
    {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Recipe id must not be empty.", nameof(id));
        }
        ArgumentNullException.ThrowIfNull(output);
        if (output.IsRawMaterial) {
            throw new ArgumentException(
                $"Material '{output.Id}' is a raw material (level 0) and cannot have a recipe.", nameof(output));
        }
        if (outputQuantity <= 0) {
            throw new ArgumentOutOfRangeException(nameof(outputQuantity), outputQuantity, "Recipe output quantity must be positive.");
        }
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) {
            throw new ArgumentException("Recipe must have at least one input.", nameof(inputs));
        }
        if (inputs.Any(input => input.Material == output)) {
            throw new ArgumentException("Recipe output must not be one of its own inputs.", nameof(inputs));
        }
        if (productionRate <= 0) {
            throw new ArgumentOutOfRangeException(nameof(productionRate), productionRate, "Recipe production rate must be positive.");
        }

        Id = id;
        Output = output;
        OutputQuantity = outputQuantity;
        Inputs = inputs;
        ProductionRate = productionRate;
    }

    /// <summary>Материалы, напрямую потребляемые рецептом (один уровень разложения, без рекурсии).</summary>
    public IReadOnlyList<Material> DirectInputMaterials => Inputs.Select(input => input.Material).ToList();
}
