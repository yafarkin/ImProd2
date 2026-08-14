namespace Game.Balancing;

/// <summary>
/// Единый JSON-отчёт по прогону утилиты (Блок 7.3.6, BUILD_PLAN «Фаза 7») — то, ради чего затевался
/// весь Блок 7.3: компактный артефакт с математикой (не трассами действий ботов), который можно
/// целиком отдать на анализ без доступа к исходным партиям. CSV прежних блоков (7.3.2-7.3.4) был
/// сознательной временной заглушкой на пути сюда, не альтернативным форматом — см. решение сессии
/// 2026-08-14 в этом же чате.
/// </summary>
public sealed record BalancingRunReport
{
    /// <summary>Метаданные версии — какой конфиг, каким кодом, когда (см. doc-comment <see cref="RunMetadata"/>).</summary>
    public required RunMetadata Metadata { get; init; }

    /// <summary>
    /// Идеальный зал X(t) (Блок 7.3.4) — присутствует всегда: и как самостоятельный результат режима
    /// <c>--mode ideal-hall</c>, и как опорная линия сходимости сетки (<see cref="Grid"/>) в режиме
    /// <c>--mode grid</c>.
    /// </summary>
    public required IdealHallSection IdealHall { get; init; }

    /// <summary>Сетка ботовых стратегий (Блок 7.3.2/7.3.5) — <c>null</c> в режиме <c>--mode ideal-hall</c>, где сетки не было.</summary>
    public GridSection? Grid { get; init; }
}

/// <summary>
/// Метаданные версии прогона (Блок 7.3.6) — без них JSON, полученный через месяцы/годы, нельзя
/// достоверно привязать к тому, каким конфигом и каким кодом он получен.
/// </summary>
public sealed record RunMetadata
{
    /// <summary>Путь конфига, как он был передан в <c>--config</c> (или выбран интерактивно).</summary>
    public required string ConfigPath { get; init; }

    /// <summary>Путь файла сессионных параметров, если использовался отдельно от <see cref="ConfigPath"/> (см. <c>ConfigSelector</c>).</summary>
    public string? SessionPath { get; init; }

    /// <summary>Режим прогона — <c>"grid"</c> или <c>"ideal-hall"</c> (см. <see cref="RunMode"/>).</summary>
    public required string Mode { get; init; }

    /// <summary>Id пресета длительности сессии.</summary>
    public required string PresetId { get; init; }

    /// <summary><c>SessionPresetConfig.MaxTurns</c> пресета — на сколько ходов посчитан идеальный зал.</summary>
    public required int MaxTurns { get; init; }

    /// <summary>Секторы цепочки — код и имя, для читаемости остального отчёта без обращения к исходному конфигу.</summary>
    public required IReadOnlyList<SectorSummary> Sectors { get; init; }

    /// <summary>Git-коммит репозитория на момент прогона (<see cref="GitCommitReader"/>); <c>null</c>, если определить не удалось (не git-репозиторий, нет <c>git</c> в PATH).</summary>
    public string? GitCommit { get; init; }

    /// <summary>Момент запуска (UTC).</summary>
    public required DateTimeOffset GeneratedAtUtc { get; init; }
}

/// <summary>Код и имя сектора — часть <see cref="RunMetadata"/>.</summary>
public sealed record SectorSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>Идеальный зал X(t) (Блок 7.3.4) — по каждому сектору полный ряд по ходам, индекс 0 = ход 1.</summary>
public sealed record IdealHallSection
{
    public required IReadOnlyDictionary<string, IReadOnlyList<decimal>> ValueByTurnBySector { get; init; }
}

/// <summary>Сетка ботовых стратегий целиком (Блок 7.3.2) — параметры прогона + сводка по каждой ячейке.</summary>
public sealed record GridSection
{
    /// <summary>Число уровней на каждую ось (<c>leverage</c>×<c>profile</c> — квадратная сетка, см. <see cref="Game.Bots.StrategyGridRunner.UniformLevels"/>).</summary>
    public required int GridSteps { get; init; }

    /// <summary>Партий на ячейку.</summary>
    public required int SessionsPerCell { get; init; }

    /// <summary>Команд на сектор в каждой партии.</summary>
    public required int TeamsPerSector { get; init; }

    public required IReadOnlyList<GridCellSummary> Cells { get; init; }
}

/// <summary>
/// Сводка по одной ячейке сетки (Блок 7.3.2/7.3.5) — только агрегаты <see
/// cref="Game.Bots.BalancingReport"/>, без единой трассы действий ботов (BUILD_PLAN Блок 7.3.6: «только
/// математика для того, чтобы увидеть проблему»).
/// </summary>
public sealed record GridCellSummary
{
    public required decimal Leverage { get; init; }
    public required decimal Profile { get; init; }
    public required int SessionCount { get; init; }

    /// <summary>См. <see cref="Game.Bots.BalancingReport.ForcedLoanShare"/>.</summary>
    public required decimal ForcedLoanShare { get; init; }

    /// <summary>См. <see cref="Game.Bots.BalancingReport.ForcedRepairEventShare"/>.</summary>
    public required decimal ForcedRepairEventShare { get; init; }

    /// <summary>См. <see cref="Game.Bots.BalancingReport.AverageFinalScoreSpread"/>.</summary>
    public required decimal AverageFinalScoreSpread { get; init; }

    /// <summary>См. <see cref="Game.Bots.BalancingReport.OverallAverageFinalConvergence"/> — одно число на ячейку для тепловой карты.</summary>
    public decimal? OverallAverageFinalConvergence { get; init; }

    /// <summary>См. <see cref="Game.Bots.BalancingReport.AverageFinalConvergenceSpread"/> — разброс между ветками внутри этой ячейки.</summary>
    public decimal? AverageFinalConvergenceSpread { get; init; }

    /// <summary>См. <see cref="Game.Bots.BalancingReport.AverageFinalConvergenceBySector"/> — какая именно ветка отстаёт в этой ячейке.</summary>
    public required IReadOnlyDictionary<string, decimal> AverageFinalConvergenceBySector { get; init; }

    /// <summary>
    /// Score(t)/X(t), усреднённая по партиям ячейки, по ходам (<c>docs/balancing-bots.md</c> §3,
    /// «Траектория по времени для дебрифа») — индекс 0 = ход 1; <c>null</c> на ходах, где ни у одной
    /// партии ячейки не было содержательной сходимости (X(t) ещё не вышел в плюс, см. doc-comment
    /// <c>BalancingHarness</c>).
    /// </summary>
    public required IReadOnlyList<decimal?> ConvergenceByTurn { get; init; }
}
