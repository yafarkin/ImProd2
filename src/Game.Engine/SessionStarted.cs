namespace Game.Engine;

/// <summary>
/// Сессия начата: ход окончания разыгран жеребьёвкой в диапазоне пресета и зафиксирован в журнале
/// (SPEC §4) — это первая запись в истории сессии, точный ход окончания не сообщается игрокам.
/// </summary>
public sealed record SessionStarted : Change<GameSessionState>
{
    /// <summary>Код пресета длительности, из диапазона которого был разыгран <see cref="EndTurn"/>.</summary>
    public required string PresetId { get; init; }

    /// <summary>Разыгранный ход окончания игры.</summary>
    public required int EndTurn { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.PresetId = PresetId;
        state.EndTurn = EndTurn;
        state.CurrentTurn = 1;
        state.CurrentPhase = TurnPhase.Calculation;
        state.PhaseExtensionSeconds = TimeSpan.Zero;
        state.IsPaused = false;
        state.IsFinished = false;
    }
}
