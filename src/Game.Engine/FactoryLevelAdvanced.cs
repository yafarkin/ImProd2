namespace Game.Engine;

/// <summary>
/// Накопленные R&amp;D-вложения в фабрику достигли порога следующего уровня (SPEC §5.8) — следствие
/// <see cref="RndInvested"/>, но отдельное событие: решение вложить деньги и факт перехода уровня
/// не одно и то же (AGENTS-память о трассируемости причин).
/// </summary>
public sealed record FactoryLevelAdvanced : Change<GameSessionState>
{
    /// <summary>Команда, чья фабрика перешла на новый уровень.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, перешедшая на новый уровень.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Уровень, на который перешла фабрика.</summary>
    public required int NewLevel { get; init; }

    public override void Apply(GameSessionState state)
    {
        var factory = state.Teams[TeamId].Factories.Single(f => f.Id == FactoryId);
        factory.AdvanceLevel();
    }
}
