namespace Game.Engine;

/// <summary>
/// Ведущий вручную скорректировал цену материала (Блок 9.6, SPEC §9.5) — минуя обычный пересчёт
/// <see cref="MarketCalculator"/>; ёмкость и счётчик проданного объёма не трогает.
/// </summary>
public sealed record MarketPriceAdjusted : Change<GameSessionState>
{
    /// <summary>Код материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>Новая цена за единицу.</summary>
    public required decimal NewPrice { get; init; }

    public override void Apply(GameSessionState state) => state.Market.AdjustPrice(MaterialId, NewPrice);
}
