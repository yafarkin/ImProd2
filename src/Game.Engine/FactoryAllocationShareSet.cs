namespace Game.Engine;

/// <summary>
/// Команда поменяла долю фабрики при разборе дефицитного сырья, общего с другими её фабриками (см.
/// doc-comment <see cref="Game.Domain.Factory.AllocationShare"/>) — запрос пользователя «как указать,
/// какое количество или % отправить на следующую фабрику»: несколько фабрик команды могут
/// претендовать на один и тот же материал (несколько экземпляров одного типа или просто разные
/// рецепты с общим сырьём), и без явной доли раздел был бы неявным и непредсказуемым.
/// </summary>
public sealed record FactoryAllocationShareSet : Change<GameSessionState>
{
    /// <summary>Команда, которой принадлежит фабрика.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, которой меняют долю.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Новая доля.</summary>
    public required decimal Share { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.SetAllocationShare(Share);
    }
}
