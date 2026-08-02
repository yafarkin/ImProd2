namespace Game.Engine.Tests;

/// <summary>Дополнительный заём по решению команды через <see cref="GameSession"/> (Блок 9.2, SPEC §5.9).</summary>
public class GameSessionLoanTests
{
    private static (GameSession Session, Ulid TeamId) StartInDecisionPhase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(startingLoan: 0m);
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        return (session, teamId);
    }

    [Fact]
    public void TakeLoan_Appends_A_LoanTaken_Event_And_Increases_Debt_And_Balance()
    {
        var (session, teamId) = StartInDecisionPhase();

        var entry = session.TakeLoan(teamId, 500m);

        var loanTaken = Assert.IsType<LoanTaken>(entry.Change);
        Assert.Equal(500m, loanTaken.Amount);
        Assert.Equal(500m, session.State.Teams[teamId].Debt);
        Assert.Equal(500m, session.State.Teams[teamId].Balance);
    }

    [Fact]
    public void TakeLoan_Throws_For_An_Unknown_Team()
    {
        var (session, _) = StartInDecisionPhase();

        Assert.Throws<ArgumentException>(() => session.TakeLoan(Ulid.NewUlid(), 500m));
    }

    [Fact]
    public void TakeLoan_Throws_For_A_NonPositive_Amount()
    {
        var (session, teamId) = StartInDecisionPhase();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.TakeLoan(teamId, 0m));
    }

    [Fact]
    public void TakeLoan_Throws_Outside_The_Decision_Phase()
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(startingLoan: 0m); // Settlement, ход 1

        Assert.Throws<InvalidOperationException>(() => session.TakeLoan(teamId, 500m));
    }

    [Fact]
    public void TakeLoan_Increases_The_Effective_Rate_Seen_By_The_Next_Interest_Charge()
    {
        var (session, teamId) = StartInDecisionPhase();
        session.TakeLoan(teamId, 1000m); // ставка = BaseLoanInterestRate + LoanInterestRateGrowthPerUnitBorrowed * 1000

        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Decision -> Settlement, ход 2
        var appended = session.RunTick(new Random(1));

        var interest = Assert.IsType<LoanInterestCharged>(appended.Single(e => e.Change is LoanInterestCharged).Change);
        var loanConfig = TestGameConfig.Resolved.Raw.StartingConditions;
        var expectedRate = FinanceCalculator.CalculateEffectiveLoanRate(1000m, 0m, 100m, loanConfig);
        Assert.Equal(expectedRate, interest.Rate);
    }
}
