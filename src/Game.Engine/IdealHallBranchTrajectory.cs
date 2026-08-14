namespace Game.Engine;

/// <summary>
/// Траектория X(t) одной ветки специализации (Блок 7.3.4, <c>docs/production-balance.md</c> §3-4) —
/// вход <see cref="IdealHallCalculator.Calculate"/>.
/// </summary>
public sealed record IdealHallBranchTrajectory
{
    /// <summary>Код сектора.</summary>
    public required string SectorId { get; init; }

    /// <summary>Отображаемое имя сектора.</summary>
    public required string SectorName { get; init; }

    /// <summary>
    /// X(t) по ходам: индекс 0 — ход 1, индекс <c>Count-1</c> — последний просчитанный ход. Растёт
    /// полого в начале (дёшево ошибиться), круче к концу (дорого простаивать на дорогом переделе) —
    /// см. <c>docs/production-balance.md</c> §3.
    /// </summary>
    public required IReadOnlyList<decimal> ValueByTurn { get; init; }
}
