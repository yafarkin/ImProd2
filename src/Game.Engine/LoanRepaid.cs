using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Команда добровольно погасила часть тела долга сверх обязательного платежа (
/// <see cref="MandatoryLoanRepaymentCharged"/>) — симметричное действие к <see cref="LoanTaken"/>:
/// там команда решает занять, здесь — решает вернуть. Списывает сумму с баланса и одновременно
/// уменьшает долг на ту же сумму (в отличие от <see cref="LoanInterestCharged"/>, который трогает
/// только баланс). Порождается на расчёте <see cref="VoluntaryLoanStep"/> из <see
/// cref="LoanRepaymentRequested"/> — <see cref="Amount"/> уже урезан до реального остатка долга на
/// момент расчёта, здесь никакой проверки/урезания больше нет.
/// </summary>
public sealed record LoanRepaid : Change<GameSessionState>
{
    /// <summary>Команда, погасившая долг.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>
    /// Сумма погашения. Может быть 0 — заявка была, но реально гасить оказалось нечего (долг успел
    /// обнулиться обязательным платежом раньше в этом же расчёте); событие всё равно порождается,
    /// чтобы корректно снять заявку (<see cref="Team.ClearPendingLoanRepayRequest"/>), а не оставить
    /// её висеть на будущее.
    /// </summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        if (Amount > 0)
        {
            team.Debit(Amount);
            team.RepayLoan(Amount);
        }

        // Заявка снимается в любом случае — и когда реально погасили, и когда Amount урезался до 0
        // (см. doc-comment Amount выше). RepayLoan сама этого больше не делает (см. её doc-comment) —
        // она общая с обязательным платежом, который заявку трогать не должен.
        team.ClearPendingLoanRepayRequest();
    }
}
