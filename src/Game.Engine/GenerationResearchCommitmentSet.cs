namespace Game.Engine;

/// <summary>
/// Команда объявила, сколько выделяет на исследование следующего поколения фабрик за ход (тот же
/// приём, что <see cref="RndCommitmentSet"/> для одной фабрики, только на уровне команды) — само
/// объявление бесплатно и мгновенно; реальное списание происходит отдельно, автоматически каждый
/// ход (см. <see cref="TickFinanceStep"/>, событие <see cref="GenerationResearchInvested"/>).
/// </summary>
public sealed record GenerationResearchCommitmentSet : Change<GameSessionState>
{
    /// <summary>Команда, объявившая сумму.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Новая сумма за ход.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        team.SetGenerationResearchCommitment(Amount);
    }
}
