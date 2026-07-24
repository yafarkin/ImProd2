namespace Game.Domain;

public sealed class Factory
{
    public string Id { get; }
    public FactoryDefinition Definition { get; }
    public int Workers { get; private set; }
    public Recipe SelectedRecipe { get; private set; }
    public int Level { get; private set; }
    public decimal RndInvestment { get; private set; }

    public Factory(string id, Sector ownerSector, FactoryDefinition definition, Recipe? selectedRecipe = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Factory id must not be empty.", nameof(id));
        ArgumentNullException.ThrowIfNull(ownerSector);
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Sector != ownerSector)
            throw new ArgumentException(
                $"Team sector '{ownerSector.Id}' does not match factory definition sector '{definition.Sector.Id}'.",
                nameof(definition));

        selectedRecipe ??= definition.Recipes[0];
        if (!definition.Recipes.Contains(selectedRecipe))
            throw new ArgumentException(
                $"Recipe '{selectedRecipe.Id}' is not produced by factory definition '{definition.Id}'.",
                nameof(selectedRecipe));

        Id = id;
        Definition = definition;
        SelectedRecipe = selectedRecipe;
        Workers = 0;
        Level = 1;
        RndInvestment = 0m;
    }

    public void Hire(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Hire count must be positive.");

        Workers += count;
    }

    public void Fire(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Fire count must be positive.");
        if (count > Workers)
            throw new InvalidOperationException($"Cannot fire {count} workers, factory '{Id}' only has {Workers}.");

        Workers -= count;
    }

    public void SelectRecipe(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (!Definition.Recipes.Contains(recipe))
            throw new ArgumentException(
                $"Recipe '{recipe.Id}' is not produced by factory definition '{Definition.Id}'.", nameof(recipe));

        SelectedRecipe = recipe;
    }

    public void InvestInRnd(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "R&D investment must be positive.");

        RndInvestment += amount;
    }

    public void AdvanceLevel()
    {
        Level++;
    }
}
