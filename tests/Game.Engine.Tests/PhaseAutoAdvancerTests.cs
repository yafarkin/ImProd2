using Game.Config.Session;

namespace Game.Engine.Tests;

public class PhaseAutoAdvancerTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly PhaseTimingConfig Timing = new()
    {
        CalculationPhaseSeconds = 5,
        DecisionPhaseSeconds = 300,
        CompletionPhaseSeconds = 15,
    };

    private static GameSession StartSession(int endTurn = 999, Func<DateTimeOffset>? clock = null)
    {
        var config = TestGameConfig.BuildWithPhaseTiming(Timing);
        return GameSession.StartWithEndTurn(config, "test", endTurn, Array.Empty<TeamSpec>(), clock: clock ?? (() => Epoch));
    }

    [Fact]
    public void Does_Nothing_While_Time_Remains_In_The_Current_Phase()
    {
        var session = StartSession();

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(1), new Random(1));

        Assert.False(acted);
        Assert.Equal(TurnPhase.Calculation, session.State.CurrentPhase);
        Assert.Empty(session.Entries.Skip(1));
    }

    [Fact]
    public void Does_Nothing_While_Paused_Even_If_Time_Is_Up()
    {
        var session = StartSession();
        session.Pause();

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(999), new Random(1));

        Assert.False(acted);
        Assert.Equal(TurnPhase.Calculation, session.State.CurrentPhase);
    }

    [Fact]
    public void Does_Nothing_Once_The_Session_Has_Finished()
    {
        var session = StartSession(endTurn: 1);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Decision
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Closing
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // finished
        Assert.True(session.State.IsFinished);

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromDays(1), new Random(1));

        Assert.False(acted);
    }

    [Fact]
    public void Advances_Decision_To_Closing_Without_Running_A_Tick()
    {
        var session = StartSession();
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // -> Decision

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(300), new Random(1));

        Assert.True(acted);
        Assert.Equal(TurnPhase.Closing, session.State.CurrentPhase);
        Assert.DoesNotContain(session.Entries, e => e.Change is MarketUpdated);
    }

    [Fact]
    public void Runs_The_Tick_And_Advances_When_The_Calculation_Phase_Expires()
    {
        var session = StartSession();

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(5), new Random(1));

        Assert.True(acted);
        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Contains(session.Entries, e => e.Change is MarketUpdated);
        Assert.Contains(session.Entries, e => e.Change is PhaseAdvanced advanced && advanced.Trigger == PhaseTransitionTrigger.Timer);
    }

    [Fact]
    public void Does_Not_Run_The_Tick_Twice_If_It_Already_Ran_Before_A_Crash_Between_Tick_And_Advance()
    {
        var session = StartSession();
        session.RunTick(new Random(1)); // simulates a tick that ran, then the process crashed before AdvancePhase
        var marketUpdatesBefore = session.Entries.Count(e => e.Change is MarketUpdated);

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(5), new Random(1));

        Assert.True(acted);
        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Equal(marketUpdatesBefore, session.Entries.Count(e => e.Change is MarketUpdated));
    }

    [Fact]
    public void Reaching_Closing_Of_The_End_Turn_Finishes_The_Session_And_Further_Calls_Are_No_Ops()
    {
        var session = StartSession(endTurn: 1);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // -> Decision
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // -> Closing

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(15), new Random(1));

        Assert.True(acted);
        Assert.True(session.State.IsFinished);

        var actedAgain = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromDays(1), new Random(1));
        Assert.False(actedAgain);
    }
}
