namespace Game.Engine;

/// <summary>
/// Фаза хода (SPEC §4): расчёт → решения → завершение (короткое read-only окно перед
/// следующим ходом). Решения команд допустимы только в фазе <see cref="Decision"/>.
/// </summary>
public enum TurnPhase
{
    /// <summary>Атомарный расчёт тика для всех команд сразу; решения команд не принимаются.</summary>
    Calculation,

    /// <summary>Команды корректируют производство, ведут переговоры, заключают контракты.</summary>
    Decision,

    /// <summary>Короткое read-only окно перед фиксацией хода — действия команд отклоняются.</summary>
    Closing
}
