namespace Game.Config;

/// <summary>
/// Пресет длительности сессии (SPEC §4): короткая/средняя/полная игра. Точный ход окончания
/// выбирается жеребьёвкой в диапазоне [<see cref="MinTurns"/>, <see cref="MaxTurns"/>] на старте сессии.
/// </summary>
public sealed record SessionPresetConfig
{
    /// <summary>Уникальный код пресета (например, "short", "medium", "full").</summary>
    public required string Id { get; init; }

    /// <summary>Отображаемое имя пресета.</summary>
    public required string Name { get; init; }

    /// <summary>Нижняя граница диапазона хода окончания игры.</summary>
    public required int MinTurns { get; init; }

    /// <summary>Верхняя граница диапазона хода окончания игры.</summary>
    public required int MaxTurns { get; init; }

    /// <summary>Ориентировочная длительность одного хода в минутах; требует калибровки.</summary>
    public required int TurnDurationMinutes { get; init; }
}
