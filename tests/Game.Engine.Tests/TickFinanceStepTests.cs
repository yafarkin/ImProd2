namespace Game.Engine.Tests;

public class TickFinanceStepTests
{
    // TestGameConfig: BaseLoanInterestRate=0.05, LoanInterestRateGrowthPerUnitBorrowed=0,
    // ForcedLoanPenaltyRatePerOccurrence=0.1, MaxReputationRatePenalty=0.1; BaseWorkerCount=5, SalaryPerWorkerPerTurn=5.
    private static readonly Config.Session.StartingConditionsConfig LoanConfig = TestGameConfig.Resolved.Raw.StartingConditions;
    private static readonly Config.Economy.WorkerProductivityConfig WorkerConfig = TestGameConfig.Resolved.Raw.WorkerProductivity;

    [Fact]
    public void Run_Returns_Nothing_For_A_Debt_Free_Team_With_No_Workers()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, reputationPercentage: 100m);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Charges_Interest_Then_Salaries_In_That_Order_When_Balance_Covers_Both()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m); // проценты = 1000 * 0.05 = 50
        team.Credit(1000m); // с запасом
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 4 * 5 = 20

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, reputationPercentage: 100m);

        Assert.Equal(2, changes.Count);
        var interest = Assert.IsType<LoanInterestCharged>(changes[0]);
        Assert.Equal(50m, interest.Amount);
        var salaries = Assert.IsType<SalariesPaid>(changes[1]);
        Assert.Equal(20m, salaries.Amount);
        Assert.Equal(4, salaries.TotalWorkers);
    }

    [Fact]
    public void Run_Appends_A_Forced_Loan_When_Interest_And_Salaries_Exceed_The_Balance()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m); // проценты = 50; TakeLoan сам зачисляет сумму на баланс...
        team.Debit(1000m); // ...поэтому сразу же её и тратим, чтобы остался только реальный остаток
        team.Credit(30m); // не хватит на проценты (50)
        var factory = team.BuildFactory(Ulid.NewUlid(), TestGameConfig.Mine);
        factory.Hire(4); // зарплата = 20

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, reputationPercentage: 100m);

        Assert.Equal(3, changes.Count);
        var forcedLoan = Assert.IsType<ForcedLoanTaken>(changes[2]);
        // баланс до принудительного займа: 30 - 50 - 20 = -40
        Assert.Equal(40m, forcedLoan.Amount);
        Assert.Equal(0.1m, forcedLoan.NewPenaltyRateSurcharge);
    }

    [Fact]
    public void Run_Charges_A_Higher_Interest_Rate_When_Reputation_Is_Damaged()
    {
        var (_, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m);
        team.Credit(1000m);

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig, reputationPercentage: 0m);

        // ставка = 0.05 (база) + 0.1 (вся надбавка при 0% репутации) = 0.15; проценты = 1000 * 0.15 = 150
        var interest = Assert.IsType<LoanInterestCharged>(Assert.Single(changes));
        Assert.Equal(0.15m, interest.Rate);
        Assert.Equal(150m, interest.Amount);
    }

    [Fact]
    public void Applying_Two_Consecutive_Shortfall_Ticks_Escalates_The_Penalty_Rate_Surcharge()
    {
        var (log, team) = TestGameConfig.StartSessionWithOneTeam();
        team.TakeLoan(1000m);
        team.Debit(1000m); // баланс обнулён — весь заём уже потрачен, платить проценты нечем

        foreach (var change in TickFinanceStep.Run(team, LoanConfig, WorkerConfig, reputationPercentage: 100m))
        {
            log.Append(change);
        }
        Assert.Equal(0.1m, team.PenaltyRateSurcharge);

        foreach (var change in TickFinanceStep.Run(team, LoanConfig, WorkerConfig, reputationPercentage: 100m))
        {
            log.Append(change);
        }
        Assert.Equal(0.2m, team.PenaltyRateSurcharge); // второй принудительный заём эскалирует ещё раз

        Assert.True(log.VerifyIntegrity());
    }
}
