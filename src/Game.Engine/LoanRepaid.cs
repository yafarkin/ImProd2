namespace Game.Engine;

/// <summary>
/// Команда добровольно погасила часть тела долга сверх обязательного платежа (
/// <see cref="MandatoryLoanRepaymentCharged"/>) — симметричное действие к <see cref="LoanTaken"/>:
/// там команда решает занять, здесь — решает вернуть. Списывает сумму с баланса и одновременно
/// уменьшает долг на ту же сумму (в отличие от <see cref="LoanInterestCharged"/>, который трогает
/// только баланс).
/// </summary>
public sealed record LoanRepaid : Change<GameSessionState>
{
    /// <summary>Команда, погасившая долг.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Сумма погашения.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        team.Debit(Amount);
        team.RepayLoan(Amount);
    }
}
