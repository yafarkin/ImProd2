using Game.Domain;
using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Упрощённый биржевой стакан для ботов (Блок 7.3.1, <c>docs/balancing-bots.md</c> §1) — не
/// переговоры (это по-прежнему прерогатива живых людей, SPEC §1), а механическое сведение заявок в
/// мозге ботов: заявки на покупку по убыванию предельной цены, на продажу — по возрастанию, сделка —
/// по текущей рыночной котировке материала, тем же путём, что и раньше единственная жёстко заданная
/// пара (<c>SubmitContractProposals</c>/<c>ConfirmContract</c>), просто для многих контрагентов и
/// многих материалов сразу, не привязанная к сектору. Пересчитывается заново каждый ход решений —
/// непокрытый остаток заявки не переносится (см. doc-comment <see cref="TradeOrder"/>).
/// </summary>
public static class OrderBook
{
    /// <summary>
    /// Сводит заявки на продажу и покупку по каждому материалу отдельно и подписывает сделки на то,
    /// что сошлось. Детерминированный порядок сведения (по <see cref="Ulid"/> команды) — та же
    /// дисциплина, что и у остального движка (AGENTS правило 6).
    /// </summary>
    public static void Match(
        GameSession session,
        IReadOnlyList<TradeOrder> sellOrders,
        IReadOnlyList<TradeOrder> buyOrders,
        Random confirmationCodeRandom)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sellOrders);
        ArgumentNullException.ThrowIfNull(buyOrders);
        ArgumentNullException.ThrowIfNull(confirmationCodeRandom);

        var turn = session.State.CurrentTurn;
        var penaltyRate = session.State.Config.Raw.Contracts.DeliveryMissPenaltyRate;

        var materials = sellOrders.Select(o => o.Material)
            .Concat(buyOrders.Select(o => o.Material))
            .Distinct();

        foreach (var material in materials)
        {
            if (!session.State.Market.HasQuote(material.Id))
            {
                continue;
            }

            var price = session.State.Market.QuoteOf(material.Id).Price;

            // Остаток непокрытого объёма каждой заявки — заявка может закрыться несколькими
            // встречными сделками за один ход, если контрагентов несколько.
            var sellers = sellOrders
                .Where(o => o.Material == material && price >= o.LimitPrice)
                .OrderBy(o => o.TeamId)
                .Select(o => (Order: o, Remaining: o.Volume))
                .ToList();
            var buyers = buyOrders
                .Where(o => o.Material == material && price <= o.LimitPrice)
                .OrderBy(o => o.TeamId)
                .Select(o => (Order: o, Remaining: o.Volume))
                .ToList();

            var sellerIndex = 0;
            var buyerIndex = 0;
            while (sellerIndex < sellers.Count && buyerIndex < buyers.Count)
            {
                var (sellOrder, sellRemaining) = sellers[sellerIndex];
                var (buyOrder, buyRemaining) = buyers[buyerIndex];

                if (sellOrder.TeamId == buyOrder.TeamId)
                {
                    // Одна и та же команда не может быть у себя и продавцом, и покупателем разом —
                    // пропускаем меньшую из двух заявок, она в любом случае не найдёт себя в паре.
                    if (sellRemaining <= buyRemaining) { sellerIndex++; } else { buyerIndex++; }
                    continue;
                }

                var volume = Math.Min(sellRemaining, buyRemaining);
                if (volume > 0m)
                {
                    SignContract(session, sellOrder.TeamId, buyOrder.TeamId, material, volume, price, penaltyRate, turn, confirmationCodeRandom);
                }

                sellRemaining -= volume;
                buyRemaining -= volume;
                sellers[sellerIndex] = (sellOrder, sellRemaining);
                buyers[buyerIndex] = (buyOrder, buyRemaining);

                if (sellRemaining <= 0m) { sellerIndex++; }
                if (buyRemaining <= 0m) { buyerIndex++; }
            }
        }
    }

    private static void SignContract(
        GameSession session, Ulid sellerTeamId, Ulid buyerTeamId, Material material, decimal volume,
        decimal unitPrice, decimal penaltyRate, int turn, Random confirmationCodeRandom)
    {
        var terms = new ContractTerms(
            ContractType.Spot, material, volume, unitPrice,
            penaltyRate: penaltyRate, effectiveTurn: turn, spotDeliveryTurn: turn + 1, recurringEndTurn: null);

        var sellerProposal = new ContractProposal(buyerTeamId, sellerTeamId, sellerTeamId, terms);
        var buyerProposal = new ContractProposal(buyerTeamId, sellerTeamId, buyerTeamId, terms);

        var result = session.SubmitContractProposals(sellerProposal, buyerProposal, confirmationCodeRandom);
        if (result.IsMatched)
        {
            // sellerProposal подана как proposalA -> продавец инициатор, подтверждает покупатель
            // (тот же приём, что раньше был в SimpleBot.TrySignSimpleContract).
            session.ConfirmContract(result.Contract!.Id, TeamRole.Manager, buyerTeamId);
        }
    }
}
