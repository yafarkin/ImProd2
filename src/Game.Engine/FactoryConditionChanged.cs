namespace Game.Engine;

/// <summary>
/// Рутинный декей фабрики за ход вне простоя (SPEC §5.6) — команда не запросила капремонт этот ход,
/// либо ждать нечего (фабрика уже в идеальном состоянии). Единственный способ противостоять этому —
/// запросить капремонт (см. <see cref="FactoryOverhaulStarted"/>), автосписания на восстановление
/// больше нет.
/// </summary>
public sealed record FactoryConditionChanged : Change<GameSessionState>
{
    /// <summary>Команда, которой принадлежит фабрика.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, чьё состояние изменилось.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Состояние на начало хода.</summary>
    public required decimal PreviousCondition { get; init; }

    /// <summary>Состояние после декея этого хода.</summary>
    public required decimal NewCondition { get; init; }

    /// <summary>Сколько состояния потеряно декеем этого хода — для аудита.</summary>
    public required decimal DecayApplied { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.ApplyConditionChange(NewCondition);
    }
}
