namespace Game.Bots;

/// <summary>
/// Одна отметка прогресса прогона сетки стратегий (Блок 7.3.2) — вход heartbeat-колбэка <see
/// cref="StrategyGridRunner.Run"/>: прогоны рассчитаны на часы без вмешательства, консоль обязана
/// периодически показывать, что процесс жив, а не зависать молча (запрос пользователя).
/// </summary>
public sealed record StrategyGridProgress
{
    /// <summary>Порядковый номер текущей ячейки сетки, 1-based.</summary>
    public required int CellIndex { get; init; }

    /// <summary>Всего ячеек в сетке (произведение числа уровней <c>leverage</c> и <c>profile</c>).</summary>
    public required int TotalCells { get; init; }

    /// <summary><c>leverage</c> текущей ячейки.</summary>
    public required decimal Leverage { get; init; }

    /// <summary><c>profile</c> текущей ячейки.</summary>
    public required decimal Profile { get; init; }

    /// <summary>Порядковый номер только что завершённой партии внутри текущей ячейки, 1-based.</summary>
    public required int SessionIndex { get; init; }

    /// <summary>Всего партий на одну ячейку.</summary>
    public required int SessionsPerCell { get; init; }

    /// <summary>Время с начала всего прогона сетки.</summary>
    public required TimeSpan Elapsed { get; init; }
}
