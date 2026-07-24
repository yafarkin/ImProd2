namespace Game.Domain;

public sealed class FactoryDefinition
{
    public string Id { get; }
    public string Name { get; }
    public Sector Sector { get; }
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
