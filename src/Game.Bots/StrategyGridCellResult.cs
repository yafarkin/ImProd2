namespace Game.Bots;

/// <summary>Сводка по одной ячейке сетки стратегий (Блок 7.3.2) — <see cref="Leverage"/>/<see cref="Profile"/> ячейки плюс обычный <see cref="BalancingReport"/> по всем её партиям.</summary>
public sealed record StrategyGridCellResult
{
    /// <summary><c>leverage</c> этой ячейки (0..1, доля пути между полюсами — см. doc-comment <see cref="SimpleBot"/>).</summary>
    public required decimal Leverage { get; init; }

    /// <summary><c>profile</c> этой ячейки (0..1).</summary>
    public required decimal Profile { get; init; }

    /// <summary>Сводка по всем партиям, прогнанным в этой ячейке.</summary>
    public required BalancingReport Report { get; init; }
}
