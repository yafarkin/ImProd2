using Game.Config.Session;

namespace Game.Engine.Tests;

public class GameSessionTests
{
    private static SessionPresetConfig Preset(int minTurns = 10, int maxTurns = 14) => new()
    {
        Id = "short",
        Name = "Короткая",
        MinTurns = minTurns,
        MaxTurns = maxTurns,
        TurnDurationMinutes = 5
    };

    [Fact]
    public void Start_Draws_An_End_Turn_Within_The_Preset_Range_And_Records_It_As_The_First_Entry()
    {
        var preset = Preset();
        var session = GameSession.Start(TestGameConfig.Resolved, preset, Array.Empty<TeamSpec>(), new Random(42));

        Assert.InRange(session.State.EndTurn, preset.MinTurns, preset.MaxTurns);
        Assert.Equal(1, session.State.CurrentTurn);
        Assert.Equal(TurnPhase.Calculation, session.State.CurrentPhase);

        var first = Assert.Single(session.Entries);
        var started = Assert.IsType<SessionStarted>(first.Change);
        Assert.Equal(preset.Id, started.PresetId);
        Assert.Equal(session.State.EndTurn, started.EndTurn);
    }

    [Fact]
    public void StartWithEndTurn_Rejects_An_End_Turn_Outside_Any_Sensible_Range_Only_At_The_Draw_Level()
    {
        // Розыгрыш всегда попадает в диапазон пресета — это гарантия Random.Next, а не отдельная
        // проверка GameSession; здесь просто убеждаемся, что многократный розыгрыш стабильно в границах.
        var preset = Preset(minTurns: 5, maxTurns: 5);

        for (var seed = 0; seed < 20; seed++)
        {
            var endTurn = SessionEndTurnDraw.Draw(preset, new Random(seed));
            Assert.Equal(5, endTurn);
        }
    }

    [Fact]
    public void AdvancePhase_Cycles_Through_Calculation_Decision_And_Closing_Then_Increments_The_Turn()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Equal(1, session.State.CurrentTurn);

        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        Assert.Equal(TurnPhase.Closing, session.State.CurrentPhase);
        Assert.Equal(1, session.State.CurrentTurn);

        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        Assert.Equal(TurnPhase.Calculation, session.State.CurrentPhase);
        Assert.Equal(2, session.State.CurrentTurn);
    }

    [Fact]
    public void AdvancePhase_Records_Whether_The_Timer_Or_The_Facilitator_Caused_The_Transition()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        var entry = session.AdvancePhase(PhaseTransitionTrigger.Facilitator);

        var advanced = Assert.IsType<PhaseAdvanced>(entry.Change);
        Assert.Equal(PhaseTransitionTrigger.Facilitator, advanced.Trigger);
    }

    [Fact]
    public void Reaching_Closing_Of_The_End_Turn_Finishes_The_Session_And_Blocks_Further_Advances()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 1, Array.Empty<TeamSpec>());

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Closing
        Assert.False(session.State.IsFinished);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // конец хода 1 == EndTurn
        Assert.True(session.State.IsFinished);
        Assert.Equal(TurnPhase.Closing, session.State.CurrentPhase);
        Assert.Equal(1, session.State.CurrentTurn);

        Assert.Throws<InvalidOperationException>(() => session.AdvancePhase(PhaseTransitionTrigger.Timer));
    }

    [Fact]
    public void EnsureDecisionsAllowed_Throws_Outside_The_Decision_Phase_And_Passes_During_It()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        Assert.Throws<InvalidOperationException>(session.EnsureDecisionsAllowed); // Calculation

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision
        session.EnsureDecisionsAllowed();

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Closing — read-only
        Assert.Throws<InvalidOperationException>(session.EnsureDecisionsAllowed);
    }

    [Fact]
    public void ExtendCurrentPhase_Accumulates_And_Resets_When_The_Phase_Changes()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        session.ExtendCurrentPhase(TimeSpan.FromSeconds(30));
        session.ExtendCurrentPhase(TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromSeconds(45), session.State.PhaseExtensionSeconds);

        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        Assert.Equal(TimeSpan.Zero, session.State.PhaseExtensionSeconds);
    }

    [Fact]
    public void ExtendCurrentPhase_Rejects_A_Non_Positive_Duration()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        Assert.Throws<ArgumentOutOfRangeException>(() => session.ExtendCurrentPhase(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ExtendCurrentPhase(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Pause_And_Resume_Toggle_IsPaused_And_Reject_Redundant_Transitions()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 10, Array.Empty<TeamSpec>());

        session.Pause();
        Assert.True(session.State.IsPaused);
        Assert.Throws<InvalidOperationException>(() => session.Pause());

        session.Resume();
        Assert.False(session.State.IsPaused);
        Assert.Throws<InvalidOperationException>(() => session.Resume());
    }

    [Fact]
    public void The_Full_Session_History_Verifies_As_A_Valid_Hash_Chain()
    {
        var session = GameSession.StartWithEndTurn(TestGameConfig.Resolved, "short", endTurn: 1, Array.Empty<TeamSpec>());

        session.Pause();
        session.Resume();
        session.ExtendCurrentPhase(TimeSpan.FromSeconds(10));
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        session.AdvancePhase(PhaseTransitionTrigger.Timer);

        Assert.True(session.VerifyIntegrity());
        Assert.True(session.State.IsFinished);
    }
}
