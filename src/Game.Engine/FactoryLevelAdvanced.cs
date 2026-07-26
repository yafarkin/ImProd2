using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Накопленные R&amp;D-вложения в фабрику достигли порога следующего уровня (SPEC §5.8) — следствие
/// <see cref="RndInvested"/>, но отдельное событие: решение вложить деньги и факт перехода уровня
/// не одно и то же (AGENTS-память о трассируемости причин).
/// </summary>
public sealed record FactoryLevelAdvanced : Change<Team>
{
    /// <summary>Фабрика, перешедшая на новый уровень.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Уровень, на который перешла фабрика.</summary>
    public required int NewLevel { get; init; }

    public override void Apply(Team state)
    {
        var factory = state.Factories.Single(f => f.Id == FactoryId);
        factory.AdvanceLevel();
    }
}
