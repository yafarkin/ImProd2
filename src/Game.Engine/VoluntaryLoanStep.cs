using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Разрешение добровольных решений по кредиту, объявленных за прошедшую фазу решений (SPEC §4,
/// §5.9: решения не применяются сразу — только на расчёте). Порядок внутри команды фиксирован:
/// сначала погашение — по факту долга на этот момент расчёта, который мог уже отличаться от того,
/// что было видно в момент решения (проценты и обязательный платёж уже применены раньше тем же
/// расчётом, см. <see cref="TickFinanceStep"/>), — потом новый заём, без ограничения по сумме (SPEC
/// §5.9: риск команды самонаказывающийся через ставку, а не через потолок). Вызывается <see
/// cref="GameSession.RunTick"/> после производства и исполнения контрактов, перед <see
/// cref="ForcedLoanStep"/> — самым последним шагом всего тика, — так принудительный заём видит
/// баланс уже после добровольных решений команды, а не до них. Возвращает готовые события, не
/// применяет их.
/// </summary>
public static class VoluntaryLoanStep
{
    public static IReadOnlyList<Change<GameSessionState>> Run(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        var changes = new List<Change<GameSessionState>>();

        if (team.PendingLoanRepayAmount > 0)
        {
            // Amount может урезаться до 0 (долга уже нет к этому моменту расчёта) — событие всё
            // равно порождается, чтобы корректно снять заявку (см. doc-comment LoanRepaid.Amount).
            var repayAmount = Math.Min(team.PendingLoanRepayAmount, team.Debt);
            changes.Add(new LoanRepaid { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = repayAmount });
        }

        if (team.PendingLoanTakeAmount > 0)
        {
            changes.Add(new LoanTaken { Id = Ulid.NewUlid(), TeamId = team.Id, Amount = team.PendingLoanTakeAmount });
        }

        return changes;
    }
}
