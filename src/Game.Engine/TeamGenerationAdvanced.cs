namespace Game.Engine;

/// <summary>
/// Накопленные вложения команды в исследование достигли порога следующего поколения пирамиды сырья
/// — следствие <see cref="GenerationResearchInvested"/>, но отдельное событие: решение вложить
/// деньги и факт открытия поколения не одно и то же (AGENTS-память о трассируемости причин; тот же
/// приём, что <see cref="FactoryLevelAdvanced"/> для одной фабрики).
/// </summary>
public sealed record TeamGenerationAdvanced : Change<GameSessionState>
{
    /// <summary>Команда, разблокировавшая новое поколение.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Поколение, на которое перешла команда.</summary>
    public required int NewGeneration { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        team.AdvanceGeneration();
    }
}
