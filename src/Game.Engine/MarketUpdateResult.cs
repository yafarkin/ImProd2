using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Результат расчёта состояния внешней экономики на один ход (Блок 6.1, SPEC §5.4-5.5). Отделён
/// от самого события <see cref="MarketUpdated"/> по тому же принципу, что и
/// <see cref="ProductionResult"/> у <see cref="ProductionCalculator"/>: расчёт — чистая функция
/// (<see cref="MarketCalculator.Calculate"/>), обёртывание в событие — забота вызывающего кода.
/// </summary>
public sealed record MarketUpdateResult
{
    /// <summary>Котировки по каждому материалу, для которого в конфиге задана базовая цена/ёмкость.</summary>
    public required IReadOnlyDictionary<string, MaterialQuote> Quotes { get; init; }

    /// <summary>Цена электричества на этот ход.</summary>
    public required decimal ElectricityPrice { get; init; }
}
