using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Команда не смогла покрыть расходы хода — система автоматически выдала принудительный заём на
/// недостающую сумму (SPEC §5.9). В отличие от <see cref="LoanTaken"/> (решение команды) — это
/// решение движка, отдельный факт со своей причиной: баланс ушёл в минус. Дополнительно навсегда
/// увеличивает штрафную надбавку к ставке по всему долгу команды (SPEC: «ставка принудительного
/// займа заведомо хуже любого добровольного» — иначе появится стратегия «не платить ради дешёвого
/// капитала»).
/// </summary>
public sealed record ForcedLoanTaken : Change<Team>
{
    /// <summary>Сумма принудительного займа — ровно недостающая часть, баланс после применения равен нулю.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Штрафная надбавка команды к ставке после этого займа (накопительно, см. <see cref="Team.PenaltyRateSurcharge"/>).</summary>
    public required decimal NewPenaltyRateSurcharge { get; init; }

    public override void Apply(Team state)
    {
        state.TakeLoan(Amount);
        state.IncreasePenaltyRateSurcharge(NewPenaltyRateSurcharge - state.PenaltyRateSurcharge);
    }
}
