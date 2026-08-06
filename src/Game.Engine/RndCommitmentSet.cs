namespace Game.Engine;

/// <summary>
/// Команда объявила, сколько выделяет на R&amp;D конкретной фабрики за ход (запрос пользователя:
/// «постоянные затраты», а не разовое вложение) — само объявление бесплатно и мгновенно, как выбор
/// рецепта (<see cref="RecipeSelected"/>) или доля при дефиците сырья
/// (<see cref="FactoryAllocationShareSet"/>); реальное списание происходит отдельно, автоматически
/// каждый ход (см. <see cref="TickFinanceStep"/>, событие <see cref="RndInvested"/>).
/// </summary>
public sealed record RndCommitmentSet : Change<GameSessionState>
{
    /// <summary>Команда, которой принадлежит фабрика.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, для которой меняют сумму.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Новая сумма за ход.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.SetRndCommitment(Amount);
    }
}
