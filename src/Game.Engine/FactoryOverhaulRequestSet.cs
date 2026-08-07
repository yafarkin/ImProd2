namespace Game.Engine;

/// <summary>
/// Команда объявила (или отменила) запрос на капремонт конкретной фабрики на ближайший расчёт (SPEC
/// §5.6) — само объявление бесплатно и мгновенно, как выбор рецепта; какая именно ступень (цена,
/// простой) сработает, определяется по факту, в момент расчёта, по состоянию фабрики на тот момент
/// (см. <see cref="WearStep"/>, событие <see cref="FactoryOverhaulStarted"/>).
/// </summary>
public sealed record FactoryOverhaulRequestSet : Change<GameSessionState>
{
    /// <summary>Команда, которой принадлежит фабрика.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, для которой меняют запрос.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Новое значение запроса.</summary>
    public required bool Requested { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.SetOverhaulRequested(Requested);
    }
}
