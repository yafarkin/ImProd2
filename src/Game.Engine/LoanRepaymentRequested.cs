namespace Game.Engine;

/// <summary>
/// Команда объявила желаемую сумму добровольного погашения долга на ближайший расчёт (SPEC §4,
/// §5.9: решения не применяются сразу), сверх обязательного платежа, который и без того списывается
/// каждый ход (<see cref="MandatoryLoanRepaymentCharged"/>) — симметрично <see
/// cref="LoanTakeRequested"/>. Само объявление бесплатно и мгновенно видимое в UI; реальное
/// списание и уменьшение долга (<see cref="LoanRepaid"/>) происходят один раз, на расчёте (<see
/// cref="VoluntaryLoanStep"/>), где заявка ещё и урезается до реального остатка долга на тот момент
/// (он мог измениться относительно того, что было видно в момент решения). Последнее объявление в
/// пределах хода замещает предыдущее. <see cref="Amount"/> = 0 — заявка снята.
/// </summary>
public sealed record LoanRepaymentRequested : Change<GameSessionState>
{
    /// <summary>Команда, объявившая заявку.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Желаемая сумма погашения на ближайший расчёт; 0 — заявка снята.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Teams[TeamId].RequestLoanRepayment(Amount);
    }
}
