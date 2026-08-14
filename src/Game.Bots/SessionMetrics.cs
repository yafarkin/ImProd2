using Game.Engine;

namespace Game.Bots;

/// <summary>Метрики одной завершённой партии (Блок 7.2, харнесс балансировки).</summary>
public sealed record SessionMetrics
{
    /// <summary>Метрики по каждому ходу партии, по порядку.</summary>
    public required IReadOnlyList<TurnMetrics> Turns { get; init; }

    /// <summary>Итоговый счёт каждой команды на момент завершения партии (SPEC §5.11).</summary>
    public required IReadOnlyList<FinalScoreResult> FinalScores { get; init; }

    /// <summary>Число команд в партии — знаменатель для доли дефолтов при агрегации нескольких партий.</summary>
    public required int TeamCount { get; init; }

    /// <summary>
    /// Сходимость к идеальному залу на момент завершения партии (Блок 7.3.5) — Score(T)/X(T), по
    /// сектору, усреднённая по командам того же сектора (обычно она одна, но <c>--teams-per-sector</c>
    /// может дать несколько). Пусто, если харнесс запущен без идеального зала на входе.
    /// </summary>
    public required IReadOnlyDictionary<string, decimal> FinalConvergenceBySector { get; init; }
}
