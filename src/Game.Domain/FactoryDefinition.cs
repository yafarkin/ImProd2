namespace Game.Domain;

/// <summary>
/// Тип фабрики: описывает, какие рецепты может выпускать фабрика этого типа и к какому сектору
/// она принадлежит. Конкретная фабрика команды (<see cref="Factory"/>) строится по этому описанию.
/// </summary>
public sealed class FactoryDefinition
{
    /// <summary>Уникальный код типа фабрики.</summary>
    public string Id { get; }

    /// <summary>Отображаемое имя типа фабрики.</summary>
    public string Name { get; }

    /// <summary>Сектор, которому принадлежит этот тип фабрики.</summary>
    public Sector Sector { get; }

    /// <summary>Рецепты, доступные фабрике этого типа (выбор продукции, если их несколько).</summary>
    public IReadOnlyList<Recipe> Recipes { get; }

    public FactoryDefinition(string id, string name, Sector sector, IReadOnlyList<Recipe> recipes)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("FactoryDefinition id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("FactoryDefinition name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(sector);
        ArgumentNullException.ThrowIfNull(recipes);
        if (recipes.Count == 0)
            throw new ArgumentException("FactoryDefinition must produce at least one recipe.", nameof(recipes));

        var mismatched = recipes.FirstOrDefault(recipe => recipe.Output.Sector != sector);
        if (mismatched is not null)
            throw new ArgumentException(
                $"Recipe '{mismatched.Id}' produces material for sector '{mismatched.Output.Sector.Id}', " +
                $"which does not match factory sector '{sector.Id}'.",
                nameof(recipes));

        Id = id;
        Name = name;
        Sector = sector;
        Recipes = recipes;
    }
}
