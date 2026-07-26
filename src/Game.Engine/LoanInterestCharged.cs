namespace Game.Engine;

/// <summary>
/// Начислены проценты по текущему долгу команды за ход (SPEC §5.9). Списывается с баланса, а не
/// добавляется к долгу — простые, не капитализируемые проценты: если денег не хватает, баланс
/// уходит в минус, и это уже дело <see cref="ForcedLoanTaken"/>, а не этого события.
/// </summary>
public sealed record LoanInterestCharged : Change<GameSessionState>
{
    /// <summary>Команда, с которой списаны проценты.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Сумма начисленных процентов.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Эффективная ставка, по которой посчитана сумма (база + рост от размера долга + штрафная надбавка) — для аудита.</summary>
    public required decimal Rate { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Teams[TeamId].Debit(Amount);
    }
}
