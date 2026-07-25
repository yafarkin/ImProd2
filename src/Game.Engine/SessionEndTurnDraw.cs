using Game.Config.Session;

namespace Game.Engine;

/// <summary>
/// Жеребьёвка хода окончания игры в диапазоне пресета (SPEC §4). Требует явного
/// <see cref="Random"/> от вызывающего кода (AGENTS §2, правило 6: никакой случайности без явного
/// seed) — тесты передают засеянный экземпляр, боевой код может использовать <see cref="Random.Shared"/>,
/// но сам факт розыгрыша и его результат фиксируются событием <see cref="SessionStarted"/>, поэтому
/// воспроизведение журнала не зависит от повторной генерации случайного числа.
/// </summary>
public static class SessionEndTurnDraw
{
    /// <summary>Возвращает ход окончания, равномерно выбранный из [<see cref="SessionPresetConfig.MinTurns"/>, <see cref="SessionPresetConfig.MaxTurns"/>].</summary>
    public static int Draw(SessionPresetConfig preset, Random random)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(random);

        return random.Next(preset.MinTurns, preset.MaxTurns + 1);
    }
}
