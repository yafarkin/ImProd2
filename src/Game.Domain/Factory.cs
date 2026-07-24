namespace Game.Domain;

/// <summary>
/// Фабрика команды, построенная по <see cref="FactoryDefinition"/>. Держит рабочих, выбранный
/// продукт (рецепт), уровень и накопленные вложения в R&amp;D.
/// </summary>
public sealed class Factory
{
    /// <summary>Уникальный идентификатор фабрики, сгенерированный при её постройке.</summary>
    public Ulid Id { get; }

    /// <summary>Тип фабрики (какие рецепты доступны, к какому сектору она относится).</summary>
    public FactoryDefinition Definition { get; }

    /// <summary>Число рабочих на фабрике; никогда не отрицательно.</summary>
    public int Workers { get; private set; }

    /// <summary>Рецепт, выбранный для производства сейчас; всегда один из <see cref="FactoryDefinition.Recipes"/>.</summary>
    public Recipe SelectedRecipe { get; private set; }

    /// <summary>Текущий уровень фабрики (открывается через R&amp;D).</summary>
    public int Level { get; private set; }

    /// <summary>Накопленные вложения в R&amp;D этой фабрики.</summary>
    public decimal RndInvestment { get; private set; }

    public Factory(Ulid id, Sector ownerSector, FactoryDefinition definition, Recipe? selectedRecipe = null)
    {
        if (id == Ulid.Empty)
        {
            throw new ArgumentException("Factory id must not be empty.", nameof(id));
        }
        ArgumentNullException.ThrowIfNull(ownerSector);
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Sector != ownerSector)
        {
            throw new ArgumentException(
                $"Team sector '{ownerSector.Id}' does not match factory definition sector '{definition.Sector.Id}'.",
                nameof(definition));
        }

        selectedRecipe ??= definition.Recipes[0];
        if (!definition.Recipes.Contains(selectedRecipe))
        {
            throw new ArgumentException(
                $"Recipe '{selectedRecipe.Id}' is not produced by factory definition '{definition.Id}'.",
                nameof(selectedRecipe));
        }

        Id = id;
        Definition = definition;
        SelectedRecipe = selectedRecipe;
        Workers = 0;
        Level = 1;
        RndInvestment = 0m;
    }

    /// <summary>Нанимает указанное число рабочих.</summary>
    public void Hire(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Hire count must be positive.");
        }

        Workers += count;
    }

    /// <summary>Увольняет указанное число рабочих; бросает исключение, если их больше, чем есть на фабрике.</summary>
    public void Fire(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Fire count must be positive.");
        }
        if (count > Workers)
        {
            throw new InvalidOperationException($"Cannot fire {count} workers, factory '{Id}' only has {Workers}.");
        }

        Workers -= count;
    }

    /// <summary>Переключает фабрику на другой рецепт из числа доступных её типу.</summary>
    public void SelectRecipe(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (!Definition.Recipes.Contains(recipe))
        {
            throw new ArgumentException(
                $"Recipe '{recipe.Id}' is not produced by factory definition '{Definition.Id}'.", nameof(recipe));
        }

        SelectedRecipe = recipe;
    }

    /// <summary>Добавляет вложение в R&amp;D этой фабрики.</summary>
    public void InvestInRnd(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "R&D investment must be positive.");
        }

        RndInvestment += amount;
    }

    /// <summary>Повышает уровень фабрики на единицу.</summary>
    public void AdvanceLevel()
    {
        Level++;
    }
}
