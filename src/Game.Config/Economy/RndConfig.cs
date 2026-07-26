namespace Game.Config.Economy;

/// <summary>
/// Параметры R&amp;D (SPEC §5.8): накопительные вложения в фабрику поднимают её уровень, уровень —
/// множитель к скорости производства. Числа — заглушки, требуют калибровки.
/// </summary>
public sealed record RndConfig
{
    /// <summary>
    /// Накопленные вложения, необходимые для перехода фабрики на следующий уровень: индекс 0 —
    /// сколько нужно суммарно, чтобы перейти с уровня 1 на 2; индекс 1 — с 2 на 3; и т.д.
    /// Вложения не сбрасываются между переходами (SPEC §5.8: «накопительные по ходам»).
    /// </summary>
    public required IReadOnlyList<decimal> CumulativeInvestmentThresholdsByLevel { get; init; }

    /// <summary>
    /// Прирост скорости производства фабрики (<c>Recipe.ProductionRate</c>) за каждый уровень сверх
    /// первого — например, 0.1 означает +10% за уровень.
    /// </summary>
    public required decimal ProductionRateBonusPerLevel { get; init; }
}
