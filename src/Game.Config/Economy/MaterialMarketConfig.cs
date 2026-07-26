namespace Game.Config.Economy;

/// <summary>
/// Базовые цена и ёмкость материала во внешней экономике (SPEC §5.4), от которых сценарный тренд
/// сессии отсчитывает изменение по ходам (см. <see cref="EconomyTrendPhaseConfig"/>). Используется
/// и аварийной закупкой (SPEC §5.3: множитель к текущей рыночной цене), и продажей системе.
/// Значения — заглушки, требуют калибровки.
/// </summary>
public sealed record MaterialMarketConfig
{
    /// <summary>Код материала (<see cref="Game.Config.Catalog.MaterialConfig.Id"/>).</summary>
    public required string MaterialId { get; init; }

    /// <summary>Базовая цена за единицу на первый ход сессии, до применения тренда.</summary>
    public required decimal BasePrice { get; init; }

    /// <summary>Базовая ёмкость (сколько единиц система выкупит по полной цене за ход) на первый ход сессии.</summary>
    public required decimal BaseCapacity { get; init; }
}
