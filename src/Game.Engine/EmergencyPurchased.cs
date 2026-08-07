namespace Game.Engine;

/// <summary>
/// Команда закупила материал напрямую у системы по аварийной цене (SPEC §5.3): цена — текущая
/// рыночная котировка материала (Блок 6.1) × множитель, служит потолком монопольных цен, потому
/// что доступна всегда и следует за живой ценой, а не константой. Товар зачисляется на склад,
/// деньги списываются. Стоимость вычислена до записи и несётся событием. Порождается на расчёте
/// <see cref="EmergencyPurchaseStep"/> из <see cref="EmergencyPurchaseRequested"/> (SPEC §4);
/// доступность (флаг конфига) и фаза проверяются раньше, при самой заявке, в <see
/// cref="GameSession.EmergencyPurchase"/>.
/// </summary>
public sealed record EmergencyPurchased : Change<GameSessionState>
{
    /// <summary>Команда-покупатель.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Код закупаемого материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>Закупленный объём.</summary>
    public required decimal Volume { get; init; }

    /// <summary>
    /// Ход, в который сделана закупка — нужен <c>EmergencyPurchasePressureCalculator</c>, чтобы
    /// считать затухающее по свежести «давление» недавних закупок этой команды этого материала (тот
    /// же приём, что <see cref="ContractDelivered.Turn"/>/<see cref="DeliveryMissed.Turn"/> для репутации).
    /// </summary>
    public required int Turn { get; init; }

    /// <summary>Цена за единицу по аварийной закупке (рыночная котировка × множитель) — для аудита.</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>Итоговая стоимость закупки (<see cref="Volume"/> × <see cref="UnitPrice"/>).</summary>
    public required decimal TotalCost { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var material = state.Config.Materials[MaterialId];

        team.Warehouse.Add(material, Volume, TotalCost);
        // TotalCost может обнулиться, если затяжной спад увёл цену материала в 0 (MarketCalculator
        // ограничивает её снизу нулём) — Team.Debit(0) бросил бы.
        if (TotalCost > 0)
        {
            team.Debit(TotalCost);
        }
        team.ClearPendingEmergencyPurchase(MaterialId);
    }
}
