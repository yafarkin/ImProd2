namespace Game.Config.Catalog;

/// <summary>Один вход рецепта: материал (по коду) и требуемое количество.</summary>
public sealed record RecipeInputConfig
{
    /// <summary>Код потребляемого материала (<see cref="MaterialConfig.Id"/>).</summary>
    public required string MaterialId { get; init; }

    /// <summary>Требуемое количество на один цикл производства.</summary>
    public required decimal Quantity { get; init; }
}
