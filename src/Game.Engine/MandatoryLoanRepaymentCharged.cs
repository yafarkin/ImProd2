namespace Game.Engine;

/// <summary>
/// Обязательный платёж по телу долга за ход (SPEC §5.9, <see cref="Game.Config.Session.StartingConditionsConfig.MandatoryRepaymentRatePerTurn"/>)
/// — отдельное от <see cref="LoanInterestCharged"/> событие: проценты списываются с баланса, не
/// уменьшая долг, этот платёж — наоборот, уменьшает именно долг. Списывает сумму с баланса и
/// одновременно уменьшает долг на ту же сумму; если баланса не хватает — как и на любой другой
/// расход хода, недостачу закрывает <see cref="ForcedLoanTaken"/>, а не это событие (сам платёж
/// всегда проходит на всю рассчитанную сумму).
/// </summary>
public sealed record MandatoryLoanRepaymentCharged : Change<GameSessionState>
{
    /// <summary>Команда, с которой списан обязательный платёж.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Сумма платежа (доля от долга на момент начала хода).</summary>
    public required decimal Amount { get; init; }

    /// <summary>Доля от долга, по которой посчитана сумма — для аудита, тот же приём, что и <see cref="LoanInterestCharged.Rate"/>.</summary>
    public required decimal Rate { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        team.Debit(Amount);
        team.RepayLoan(Amount);
    }
}
