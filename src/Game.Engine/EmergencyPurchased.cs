namespace Game.Engine;

/// <summary>
/// Команда закупила материал напрямую у системы по аварийной цене (SPEC §5.3): цена — системная
/// цена материала × множитель, служит потолком монопольных цен, потому что доступна всегда. Товар
/// зачисляется на склад, деньги списываются. Стоимость вычислена до записи и несётся событием.
/// Доступность (флаг конфига) и фаза проверяются до записи, в <see cref="GameSession.EmergencyPurchase"/>.
/// </summary>
public sealed record EmergencyPurchased : Change<GameSessionState>
{
    /// <summary>Команда-покупатель.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Код закупаемого материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>Закупленный объём.</summary>
    public required decimal Volume { get; init; }

    /// <summary>Цена за единицу по аварийной закупке (системная цена × множитель) — для аудита.</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>Итоговая стоимость закупки (<see cref="Volume"/> × <see cref="UnitPrice"/>).</summary>
    public required decimal TotalCost { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var material = state.Config.Materials[MaterialId];

        team.Warehouse.Add(material, Volume);
        team.Debit(TotalCost);
    }
}
