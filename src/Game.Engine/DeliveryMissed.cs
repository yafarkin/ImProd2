namespace Game.Engine;

/// <summary>
/// Продавец не смог обеспечить поставку по контракту в этом ходу (Delivery Miss, SPEC §6): штраф
/// продавца в пользу покупателя, контракт при этом продолжает действовать (для spot — его
/// единственная поставка сорвана, и он завершается). Удар по репутации не считается здесь —
/// модуль репутации (Блок 6.2, <see cref="ReputationCalculator"/>) читает это событие прямо из
/// журнала, а не по отдельному хранимому счётчику. Штраф вычислен до записи и несётся событием.
/// </summary>
public sealed record DeliveryMissed : Change<GameSessionState>
{
    /// <summary>Идентификатор контракта.</summary>
    public required Ulid ContractId { get; init; }

    /// <summary>Ход, на котором произошёл срыв — нужен модулю репутации (SPEC §7) для затухания по свежести.</summary>
    public required int Turn { get; init; }

    /// <summary>
    /// Сколько единиц не удалось поставить (для аудита и будущей репутации). Поставка «всё или
    /// ничего» (SPEC §6), так что при срыве это весь объём поставки — покупатель не получает ничего.
    /// </summary>
    public required decimal ShortfallVolume { get; init; }

    /// <summary>Сумма штрафа: сумма поставки × ставка штрафа за срыв.</summary>
    public required decimal PenaltyAmount { get; init; }

    public override void Apply(GameSessionState state)
    {
        var contract = state.Contracts[ContractId];
        var seller = state.Teams[contract.SellerTeamId];
        var buyer = state.Teams[contract.BuyerTeamId];

        if (PenaltyAmount > 0)
        {
            seller.Debit(PenaltyAmount);
            buyer.Credit(PenaltyAmount);
        }

        if (contract.Terms.Type == Domain.ContractType.Spot)
        {
            contract.Complete();
        }
    }
}
