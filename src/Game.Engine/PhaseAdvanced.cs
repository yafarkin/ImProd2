namespace Game.Engine;

/// <summary>
/// Фаза хода завершилась и сессия перешла к следующей (SPEC §4: расчёт → решения → завершение →
/// расчёт следующего хода). Порядок фаз фиксирован; <see cref="Trigger"/> различает, истёк ли
/// таймер сам по себе или ведущий принудительно ускорил переход — это разные факты, а не один и
/// тот же с додуманной причиной (AGENTS-память о трассируемости причин).
/// </summary>
public sealed record PhaseAdvanced : Change<GameSessionState>
{
    /// <summary>Что вызвало переход: истечение таймера или ручное вмешательство ведущего.</summary>
    public required PhaseTransitionTrigger Trigger { get; init; }

    public override void Apply(GameSessionState state)
    {
        if (state.CurrentPhase == TurnPhase.Closing && state.CurrentTurn == state.EndTurn)
        {
            state.IsFinished = true;
            return;
        }

        (state.CurrentPhase, state.CurrentTurn) = state.CurrentPhase switch
        {
            TurnPhase.Calculation => (TurnPhase.Decision, state.CurrentTurn),
            TurnPhase.Decision => (TurnPhase.Closing, state.CurrentTurn),
            TurnPhase.Closing => (TurnPhase.Calculation, state.CurrentTurn + 1),
            _ => throw new InvalidOperationException($"Unknown turn phase '{state.CurrentPhase}'.")
        };
        state.PhaseExtensionSeconds = TimeSpan.Zero;
    }
}
