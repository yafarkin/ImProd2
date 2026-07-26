using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Команда построила фабрику заданного типа (Блок 7.1, SPEC §5.6: постройка мгновенная — фабрика
/// начинает работать со следующего хода естественным образом, без отдельного «отложенного»
/// состояния, поскольку следующий расчёт тика увидит её уже существующей). Несёт код выбранного
/// рецепта, даже если он совпадает со значением по умолчанию, — чтобы экраны не были обязаны знать
/// правило выбора рецепта «по умолчанию» (AGENTS-память о трассируемости причин).
/// </summary>
public sealed record FactoryBuilt : Change<GameSessionState>
{
    /// <summary>Команда, построившая фабрику.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Идентификатор построенной фабрики.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Тип фабрики (<c>FactoryDefinitionConfig.Id</c>).</summary>
    public required string FactoryDefinitionId { get; init; }

    /// <summary>Выбранный при постройке рецепт (<c>RecipeConfig.Id</c>).</summary>
    public required string RecipeId { get; init; }

    /// <summary>Стоимость постройки (<c>FactoryDefinitionConfig.BuildCost</c> на момент действия).</summary>
    public required decimal Cost { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var definition = state.Config.FactoryDefinitions.Single(f => f.Id == FactoryDefinitionId);
        var recipe = definition.Recipes.Single(r => r.Id == RecipeId);

        team.BuildFactory(FactoryId, definition, recipe);
        if (Cost > 0)
        {
            team.Debit(Cost);
        }
    }
}
