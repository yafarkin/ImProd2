using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Команда переключила фабрику на другой рецепт из числа доступных её типу (Блок 9.1, SPEC §9.3:
/// управление фабриками). В отличие от <see cref="FactoryBuilt"/>, переключение не несёт стоимости —
/// SPEC не упоминает плату за смену продукта, в отличие от найма/увольнения рабочих (см.
/// <see cref="WorkersHired"/>/<see cref="WorkersFired"/>).
/// </summary>
public sealed record RecipeSelected : Change<GameSessionState>
{
    /// <summary>Команда, переключившая фабрику.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Переключаемая фабрика.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Новый выбранный рецепт (<c>RecipeConfig.Id</c>).</summary>
    public required string RecipeId { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        var recipe = factory.Definition.Recipes.Single(r => r.Id == RecipeId);

        factory.SelectRecipe(recipe);
    }
}
