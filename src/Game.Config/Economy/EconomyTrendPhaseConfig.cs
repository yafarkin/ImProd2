namespace Game.Config.Economy;

/// <summary>
/// Один отрезок сценарного тренда экономики сессии: с хода по ход действует заданный тренд,
/// который двигает цену и ёмкость каждого материала на заданную величину за ход.
/// Числа — заглушки, требуют калибровки.
/// </summary>
public sealed record EconomyTrendPhaseConfig
{
    /// <summary>Тренд, действующий на этом отрезке.</summary>
    public required EconomyTrend Trend { get; init; }

    /// <summary>Ход, с которого начинается отрезок (включительно).</summary>
    public required int StartTurn { get; init; }

    /// <summary>Ход, которым заканчивается отрезок (включительно).</summary>
    public required int EndTurn { get; init; }

    /// <summary>Изменение цены за ход на этом отрезке.</summary>
    public required decimal PriceChangePerTurn { get; init; }

    /// <summary>Изменение ёмкости за ход на этом отрезке.</summary>
    public required decimal CapacityChangePerTurn { get; init; }
}
