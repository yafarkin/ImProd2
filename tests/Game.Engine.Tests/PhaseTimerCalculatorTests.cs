using Game.Config.Session;

namespace Game.Engine.Tests;

public class PhaseTimerCalculatorTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly PhaseTimingConfig Timing = new()
    {
        CalculationPhaseSeconds = 5,
        DecisionPhaseSeconds = 300,
        CompletionPhaseSeconds = 15,
    };

    private static (GameSession Session, Action<TimeSpan> Advance) StartSession()
    {
        var config = TestGameConfig.BuildWithPhaseTiming(Timing);
        var now = Epoch;
        var session = GameSession.StartWithEndTurn(config, "test", endTurn: 999, Array.Empty<TeamSpec>(), clock: () => now);

        return (session, by => now += by);
    }

    [Fact]
    public void Remaining_Counts_Down_The_Base_Duration_Of_The_Current_Phase()
    {
        var (session, advance) = StartSession();

        Assert.Equal(TimeSpan.FromSeconds(5), PhaseTimerCalculator.Remaining(session, Epoch));

        advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(3), PhaseTimerCalculator.Remaining(session, Epoch + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Remaining_Uses_The_Base_Duration_Of_Whichever_Phase_Is_Current()
    {
        var (session, advance) = StartSession();

        advance(TimeSpan.FromSeconds(5));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // -> Decision, boundary timestamp = Epoch+5s
        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Equal(TimeSpan.FromSeconds(300), PhaseTimerCalculator.Remaining(session, Epoch + TimeSpan.FromSeconds(5)));

        advance(TimeSpan.FromSeconds(300));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // -> Closing, boundary timestamp = Epoch+305s
        Assert.Equal(TurnPhase.Closing, session.State.CurrentPhase);
        Assert.Equal(TimeSpan.FromSeconds(15), PhaseTimerCalculator.Remaining(session, Epoch + TimeSpan.FromSeconds(305)));
    }

    [Fact]
    public void Remaining_Goes_Negative_Once_The_Phase_Has_Overrun()
    {
        var (session, _) = StartSession();

        var remaining = PhaseTimerCalculator.Remaining(session, Epoch + TimeSpan.FromSeconds(9));
        Assert.Equal(TimeSpan.FromSeconds(-4), remaining);
    }

    [Fact]
    public void Remaining_Adds_The_Facilitators_Phase_Extension_To_The_Base_Duration()
    {
        var (session, _) = StartSession();

        session.ExtendCurrentPhase(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(35), PhaseTimerCalculator.Remaining(session, Epoch));
    }

    [Fact]
    public void Remaining_Excludes_Time_Spent_Paused_From_The_Elapsed_Duration()
    {
        var (session, advance) = StartSession();

        advance(TimeSpan.FromSeconds(1));
        session.Pause(); // paused at Epoch+1s
        advance(TimeSpan.FromSeconds(100));
        session.Resume(); // resumed at Epoch+101s, 100s of pause excluded

        // Wall-clock elapsed since boundary is 101s, but only 1s of it should count.
        Assert.Equal(TimeSpan.FromSeconds(4), PhaseTimerCalculator.Remaining(session, Epoch + TimeSpan.FromSeconds(101)));
    }

    [Fact]
    public void Remaining_Freezes_At_The_Moment_Of_Pausing_While_Still_Paused()
    {
        var (session, advance) = StartSession();

        advance(TimeSpan.FromSeconds(1));
        session.Pause(); // paused at Epoch+1s

        var whilePaused = PhaseTimerCalculator.Remaining(session, Epoch + TimeSpan.FromSeconds(500));
        Assert.Equal(TimeSpan.FromSeconds(4), whilePaused); // 5s base - 1s elapsed before the pause, regardless of "now"
    }

    [Fact]
    public void Remaining_Is_Zero_Once_The_Session_Has_Finished()
    {
        var config = TestGameConfig.BuildWithPhaseTiming(Timing);
        var now = Epoch;
        var session = GameSession.StartWithEndTurn(config, "test", endTurn: 1, Array.Empty<TeamSpec>(), clock: () => now);

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Closing
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // finishes at end turn

        Assert.True(session.State.IsFinished);
        Assert.Equal(TimeSpan.Zero, PhaseTimerCalculator.Remaining(session, now + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void CalculationTickAlreadyRanForCurrentPhase_Is_False_Right_After_A_Phase_Boundary()
    {
        var (session, _) = StartSession();

        Assert.False(PhaseTimerCalculator.CalculationTickAlreadyRanForCurrentPhase(session));
    }

    [Fact]
    public void CalculationTickAlreadyRanForCurrentPhase_Is_True_After_RunTick_Has_Appended_MarketUpdated()
    {
        var (session, _) = StartSession();

        session.RunTick(new Random(1));

        Assert.True(PhaseTimerCalculator.CalculationTickAlreadyRanForCurrentPhase(session));
    }

    [Fact]
    public void CalculationTickAlreadyRanForCurrentPhase_Resets_On_The_Next_Phase_Boundary()
    {
        var (session, advance) = StartSession();

        session.RunTick(new Random(1));
        advance(TimeSpan.FromSeconds(5));
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // -> Decision: fresh boundary, no MarketUpdated since it

        Assert.False(PhaseTimerCalculator.CalculationTickAlreadyRanForCurrentPhase(session));
    }
}
