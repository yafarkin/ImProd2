using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Внешняя экономика обновилась на текущий ход (Блок 6.1, SPEC §4 — «обновление рынка» идёт после
/// исполнения контрактов; SPEC §5.4-5.5): новые котировки по каждому материалу и цена
/// электричества. Несёт уже вычисленные <see cref="MarketCalculator.Calculate"/> значения, а не
/// пересчитывает их заново при применении — тот же принцип, что и у <see cref="FactoryProduced"/>:
/// экраны читают котировки прямо из события, не восстанавливая их по формуле тренда
/// (AGENTS-память о трассируемости причин).
/// </summary>
public sealed record MarketUpdated : Change<GameSessionState>
{
    /// <summary>Новые котировки по коду материала.</summary>
    public required IReadOnlyDictionary<string, MaterialQuote> Quotes { get; init; }

    /// <summary>Новая цена электричества.</summary>
    public required decimal ElectricityPrice { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Market.ReplaceQuotes(Quotes, ElectricityPrice);
    }
}
