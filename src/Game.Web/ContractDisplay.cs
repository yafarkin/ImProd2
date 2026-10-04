using Game.Domain;

namespace Game.Web;

/// <summary>
/// Подписи карточек «Сделок» (docs/manager-ui/README.md §4, блок 4) — отдельно от разметки, чтобы
/// формат сторожили тесты. Цена и сумма — в формате «за единицу» (<see cref="DashboardDisplay.FormatUnitCost"/>):
/// при ценах 0.07–0.6 ¤ округление до целого показывало 0.5 ¤ как «1 ¤» (§2 п.15).
/// </summary>
public static class ContractDisplay
{
    /// <summary>«3 ед./ход × 0.5 ¤ = 1.5 ¤/ход · штраф 10.0 % · с хода 6, бессрочно · код 8ZKPX2».</summary>
    public static string TermsLine(TeamScreen.ContractRow contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var perTurn = contract.Type == ContractType.Recurring ? "/ход" : "";
        var range = DashboardDisplay.FormatTurnRange(
            contract.Type, contract.Status, contract.EffectiveTurn, contract.SpotDeliveryTurn, contract.RecurringEndTurn);
        return $"{contract.Volume:0.##} ед.{perTurn} × {DashboardDisplay.FormatUnitCost(contract.UnitPrice)}"
            + $" = {DashboardDisplay.FormatUnitCost(contract.Volume * contract.UnitPrice)}{perTurn}"
            + $" · штраф {DashboardDisplay.FormatRate(contract.PenaltyRate)} · {range} · код {contract.ConfirmationCode}";
    }

    /// <summary>«Поставки: 7 в срок» или «Поставки: 5 в срок, 2 сорвано».</summary>
    public static string DeliveriesLine(TeamScreen.DeliveryStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        return stats.Missed == 0
            ? $"Поставки: {stats.Delivered} в срок"
            : $"Поставки: {stats.Delivered} в срок, {stats.Missed} сорвано";
    }

    /// <summary>Наша заявка: «мы → Лес: Железная руда · 40 ед. × 0.47 ¤ · подана на ходу 12».</summary>
    public static string OutgoingProposalLine(TeamScreen.ProposalRow proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        var terms = proposal.Volume is { } volume && proposal.UnitPrice is { } price
            ? $" · {volume:0.##} ед. × {DashboardDisplay.FormatUnitCost(price)}"
            : "";
        return $"{proposal.MaterialName}{terms} · подана на ходу {proposal.SubmittedOnTurn}";
    }
}
