namespace Game.Engine;

/// <summary>
/// Поставка по контракту исполнена в этом ходу: продавец передал товар со своего склада на склад
/// покупателя, покупатель оплатил (SPEC §6). Событие двустороннее по своей природе (сделка касается
/// обеих команд сразу) — в отличие от одно-командных событий производства/финансов, — поэтому
/// несёт обе стороны и переносит и товар, и деньги в одном факте. Для spot-контракта его
/// единственная поставка на этом и завершается.
/// </summary>
public sealed record ContractDelivered : Change<GameSessionState>
{
    /// <summary>Идентификатор контракта.</summary>
    public required Ulid ContractId { get; init; }

    /// <summary>Ход, на котором состоялась поставка — нужен модулю репутации (Блок 6.2, SPEC §7) для затухания по свежести.</summary>
    public required int Turn { get; init; }

    public override void Apply(GameSessionState state)
    {
        var contract = state.Contracts[ContractId];
        var terms = contract.Terms;
        var seller = state.Teams[contract.SellerTeamId];
        var buyer = state.Teams[contract.BuyerTeamId];
        var sum = terms.Volume * terms.UnitPrice;

        seller.Warehouse.Remove(terms.Material, terms.Volume);
        buyer.Warehouse.Add(terms.Material, terms.Volume);
        buyer.Debit(sum);
        seller.Credit(sum);

        if (terms.Type == Domain.ContractType.Spot)
        {
            contract.Complete();
        }
    }
}
