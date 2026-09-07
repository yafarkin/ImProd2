using Game.Config.Session;

namespace Game.Engine;

/// <summary>
/// Жеребьёвка хода окончания игры (SPEC §4). Требует явного <see cref="Random"/> от вызывающего кода
/// (AGENTS §2, правило 6: никакой случайности без явного seed) — тесты передают засеянный экземпляр,
/// боевой код может использовать <see cref="Random.Shared"/>, но сам факт розыгрыша и его результат
/// фиксируются событием <see cref="SessionStarted"/>, поэтому воспроизведение журнала не зависит от
/// повторной генерации случайного числа.
/// </summary>
public static class SessionEndTurnDraw
{
    /// <summary>Возвращает ход окончания, равномерно выбранный из [<see cref="SessionDurationConfig.MinTurns"/>, <see cref="SessionDurationConfig.MaxTurns"/>].</summary>
    public static int Draw(SessionDurationConfig duration, Random random)
    {
        ArgumentNullException.ThrowIfNull(duration);
        ArgumentNullException.ThrowIfNull(random);

        return random.Next(duration.MinTurns, duration.MaxTurns + 1);
    }
}
