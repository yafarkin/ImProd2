namespace Game.Engine;

/// <summary>
/// Состояние игровой сессии (AGENTS §2, правило 4: состояние сессии, не статика). На этом блоке —
/// только каркас фаз хода; производство, финансы, контракты и т.д. добавят свои поля в следующих
/// блоках. Сознательно не хранит реальное время (<see cref="DateTime"/>/<see cref="DateTimeOffset"/>) —
/// домен остаётся детерминированным (AGENTS §2, правило 6); фактический отсчёт секунд и определение
/// момента истечения таймера — забота внешнего серверного таймер-сервиса, который читает
/// <see cref="PhaseExtensionSeconds"/> и вызывает <see cref="GameSession.AdvancePhase"/>.
/// </summary>
public sealed class GameSessionState
{
    /// <summary>Пресет длительности сессии, по которому был разыгран <see cref="EndTurn"/>.</summary>
    public string PresetId { get; internal set; } = string.Empty;

    /// <summary>
    /// Ход, на котором игра завершится — разыгран жеребьёвкой при старте сессии и неизвестен игрокам
    /// (SPEC §4). Не показывается на экранах напрямую.
    /// </summary>
    public int EndTurn { get; internal set; }

    /// <summary>Текущий ход (нумерация с 1).</summary>
    public int CurrentTurn { get; internal set; }

    /// <summary>Текущая фаза текущего хода.</summary>
    public TurnPhase CurrentPhase { get; internal set; }

    /// <summary>
    /// Накопленное продление текущей фазы ведущим (SPEC §4: «продлить» — событие). Обнуляется при
    /// каждом переходе к следующей фазе.
    /// </summary>
    public TimeSpan PhaseExtensionSeconds { get; internal set; } = TimeSpan.Zero;

    /// <summary>Поставлена ли сессия на паузу ведущим.</summary>
    public bool IsPaused { get; internal set; }

    /// <summary>
    /// Сессия достигла <see cref="EndTurn"/> и завершена. После этого переходы фаз недопустимы —
    /// финальная фаза хода <see cref="EndTurn"/> остаётся текущей (она и так read-only).
    /// </summary>
    public bool IsFinished { get; internal set; }
}
