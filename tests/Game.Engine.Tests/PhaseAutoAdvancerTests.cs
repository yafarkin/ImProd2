using Game.Config.Session;

namespace Game.Engine.Tests;

public class PhaseAutoAdvancerTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly PhaseTimingConfig Timing = new()
    {
        SettlementPhaseSeconds = 20,
        DecisionPhaseSeconds = 300,
    };

    private static GameSession StartSession(int endTurn = 999, Func<DateTimeOffset>? clock = null)
    {
        var config = TestGameConfig.BuildWithPhaseTiming(Timing);
        return GameSession.StartWithEndTurn(config, endTurn, Array.Empty<TeamSpec>(), clock: clock ?? (() => Epoch));
    }

    [Fact]
    public void Runs_The_Tick_Immediately_On_Entering_Settlement_Without_Waiting_For_The_Timer()
    {
        var session = StartSession();

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(1), new Random(1));

        Assert.True(acted);
        Assert.Equal(TurnPhase.Settlement, session.State.CurrentPhase);
        Assert.Contains(session.Entries, e => e.Change is MarketUpdated);
    }

    [Fact]
    public void Does_Nothing_After_The_Tick_Has_Run_While_Time_Remains_In_Settlement()
    {
        var session = StartSession();
        PhaseAutoAdvancer.TryAdvance(session, Epoch, new Random(1)); // runs the tick

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(5), new Random(1));

        Assert.False(acted);
        Assert.Equal(TurnPhase.Settlement, session.State.CurrentPhase);
    }

    [Fact]
    public void Advances_To_Decision_Once_The_Settlement_Timer_Expires()
    {
        var session = StartSession();
        PhaseAutoAdvancer.TryAdvance(session, Epoch, new Random(1)); // runs the tick

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(20), new Random(1));

        Assert.True(acted);
        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Contains(session.Entries, e => e.Change is PhaseAdvanced advanced && advanced.Trigger == PhaseTransitionTrigger.Timer);
    }

    [Fact]
    public void Does_Nothing_While_Paused_Even_Before_The_Tick_Has_Run()
    {
        var session = StartSession();
        session.Pause();

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(999), new Random(1));

        Assert.False(acted);
        Assert.Equal(TurnPhase.Settlement, session.State.CurrentPhase);
        Assert.DoesNotContain(session.Entries, e => e.Change is MarketUpdated);
    }

    [Fact]
    public void Does_Nothing_Once_The_Session_Has_Finished()
    {
        var session = StartSession(endTurn: 1);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement(1) -> Decision(1)
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Decision(1) at EndTurn -> finished
        Assert.True(session.State.IsFinished);

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromDays(1), new Random(1));

        Assert.False(acted);
    }

    [Fact]
    public void Does_Not_Run_The_Tick_Twice_If_It_Already_Ran_Before_A_Crash_Before_The_Advance_Was_Recorded()
    {
        var session = StartSession();
        session.RunTick(new Random(1)); // simulates a tick that ran, then the process crashed before TryAdvance returned
        var marketUpdatesBefore = session.Entries.Count(e => e.Change is MarketUpdated);

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(20), new Random(1));

        Assert.True(acted);
        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Equal(marketUpdatesBefore, session.Entries.Count(e => e.Change is MarketUpdated));
    }

    [Fact]
    public void Reaching_The_End_Of_The_Last_Decision_Finishes_The_Session_And_Further_Calls_Are_No_Ops()
    {
        var session = StartSession(endTurn: 1);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement(1) -> Decision(1)

        var acted = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromSeconds(300), new Random(1));

        Assert.True(acted);
        Assert.True(session.State.IsFinished);

        var actedAgain = PhaseAutoAdvancer.TryAdvance(session, Epoch + TimeSpan.FromDays(1), new Random(1));
        Assert.False(actedAgain);
    }

    [Fact]
    public void Facilitator_Advance_Out_Of_An_Uncalculated_Settlement_While_Paused_Runs_The_Tick_First()
    {
        var session = StartSession();
        session.Pause();

        PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1));

        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Equal(1, session.Entries.Count(e => e.Change is MarketUpdated));
    }

    [Fact]
    public void Facilitator_Advance_Into_Settlement_While_Paused_Runs_The_Tick_Immediately()
    {
        var session = StartSession();
        session.Pause();
        PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1)); // Settlement(1) -> Decision(1)

        PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1)); // Decision(1) -> Settlement(2)

        Assert.Equal(2, session.State.CurrentTurn);
        Assert.Equal(TurnPhase.Settlement, session.State.CurrentPhase);
        Assert.True(PhaseTimerCalculator.CalculationTickAlreadyRanForCurrentPhase(session));
        Assert.Equal(2, session.Entries.Count(e => e.Change is MarketUpdated));
    }

    [Fact]
    public void Repeated_Facilitator_Advances_Calculate_Every_Turn_Exactly_Once()
    {
        var session = StartSession();
        session.Pause();

        for (var i = 0; i < 10; i++)
        {
            PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1));
        }

        // Десять переходов от Settlement(1): Decision(1), Settlement(2), ..., Decision(5), Settlement(6).
        Assert.Equal(6, session.State.CurrentTurn);
        Assert.Equal(6, session.Entries.Count(e => e.Change is MarketUpdated));
    }

    [Fact]
    public void Facilitator_Advance_Does_Not_Rerun_A_Tick_The_Timer_Already_Ran()
    {
        var session = StartSession();
        PhaseAutoAdvancer.TryAdvance(session, Epoch, new Random(1)); // the timer runs the tick

        PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1));

        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Equal(1, session.Entries.Count(e => e.Change is MarketUpdated));
    }

    [Fact]
    public void Facilitator_Advance_That_Finishes_The_Session_Runs_No_Tick()
    {
        var session = StartSession(endTurn: 1);
        PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1)); // Settlement(1) -> Decision(1)

        PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1)); // Decision(1) at EndTurn -> finished

        Assert.True(session.State.IsFinished);
        Assert.Equal(1, session.Entries.Count(e => e.Change is MarketUpdated));
    }
}
