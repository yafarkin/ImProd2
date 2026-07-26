using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Чистые правила исполнения контрактов в тике (SPEC §4, §6): решают, положена ли контракту
/// поставка на текущем ходу. Само применение (перенос товара/денег) делают события
/// <see cref="ContractDelivered"/>/<see cref="DeliveryMissed"/>, оркестровку — <see cref="GameSession.RunTick"/>.
/// </summary>
public static class ContractExecution
{
    /// <summary>
    /// Положена ли контракту поставка на ходу <paramref name="currentTurn"/>: только действующему
    /// контракту, spot — ровно на своём ходу поставки, recurring — на каждом ходу диапазона
    /// [вступление в силу, конец].
    /// </summary>
    public static bool IsDeliveryDue(Contract contract, int currentTurn)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (contract.Status != ContractStatus.Active)
        {
            return false;
        }

        var terms = contract.Terms;
        return terms.Type switch
        {
            ContractType.Spot => currentTurn == terms.SpotDeliveryTurn,
            ContractType.Recurring => currentTurn >= terms.EffectiveTurn && currentTurn <= terms.RecurringEndTurn,
            _ => false,
        };
    }
}
