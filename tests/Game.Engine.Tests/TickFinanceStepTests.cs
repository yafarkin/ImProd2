using Game.Config.Economy;
using Game.Config.Session;
using Game.Domain;

namespace Game.Engine.Tests;

public class TickFinanceStepTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Ore = new("ore", "Железная руда", SectorA, level: 0);
    private static readonly Recipe OreMining =
        new("ore-mining", Ore, outputQuantity: 1m, inputs: Array.Empty<RecipeInput>(), productionRate: 1m);
    private static readonly FactoryDefinition Mine = new("iron-mine", "Рудник", SectorA, new[] { OreMining });

    private static readonly StartingConditionsConfig LoanConfig = new()
    {
        MaxStartingLoanAmount = 10000m,
        BaseLoanInterestRate = 0.05m,
        LoanInterestRateGrowthPerUnitBorrowed = 0m,
        ForcedLoanPenaltyRatePerOccurrence = 0.1m,
    };

    private static readonly WorkerProductivityConfig WorkerConfig = new()
    {
        BaseWorkerCount = 5,
        DiminishingReturnsFactor = 0.5m,
        HireCostPerWorker = 50m,
        FireCostPerWorker = 30m,
        SalaryPerWorkerPerTurn = 5m,
    };

    private static Team NewTeam() => new(Ulid.NewUlid(), "Команда А1", SectorA);

    [Fact]
    public void Run_Returns_Nothing_For_A_Debt_Free_Team_With_No_Workers()
    {
        var team = NewTeam();

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig);

        Assert.Empty(changes);
    }

    [Fact]
    public void Run_Charges_Interest_Then_Salaries_In_That_Order_When_Balance_Covers_Both()
    {
        var team = NewTeam();
        team.TakeLoan(1000m); // проценты = 1000 * 0.05 = 50
        team.Credit(1000m); // с запасом
        var factory = team.BuildFactory(Ulid.NewUlid(), Mine);
        factory.Hire(4); // зарплата = 4 * 5 = 20

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig);

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
        var team = NewTeam();
        team.TakeLoan(1000m); // проценты = 50; TakeLoan сам зачисляет сумму на баланс...
        team.Debit(1000m); // ...поэтому сразу же её и тратим, чтобы остался только реальный остаток
        team.Credit(30m); // не хватит на проценты (50)
        var factory = team.BuildFactory(Ulid.NewUlid(), Mine);
        factory.Hire(4); // зарплата = 20

        var changes = TickFinanceStep.Run(team, LoanConfig, WorkerConfig);

        Assert.Equal(3, changes.Count);
        var forcedLoan = Assert.IsType<ForcedLoanTaken>(changes[2]);
        // баланс до принудительного займа: 30 - 50 - 20 = -40
        Assert.Equal(40m, forcedLoan.Amount);
        Assert.Equal(0.1m, forcedLoan.NewPenaltyRateSurcharge);
    }

    [Fact]
    public void Applying_Two_Consecutive_Shortfall_Ticks_Escalates_The_Penalty_Rate_Surcharge()
    {
        var team = NewTeam();
        team.TakeLoan(1000m);
        team.Debit(1000m); // баланс обнулён — весь заём уже потрачен, платить проценты нечем
        var log = new EventLog<Team>(team);

        foreach (var change in TickFinanceStep.Run(team, LoanConfig, WorkerConfig))
        {
            log.Append(change);
        }
        Assert.Equal(0.1m, team.PenaltyRateSurcharge);

        foreach (var change in TickFinanceStep.Run(team, LoanConfig, WorkerConfig))
        {
            log.Append(change);
        }
        Assert.Equal(0.2m, team.PenaltyRateSurcharge); // второй принудительный заём эскалирует ещё раз

        Assert.True(log.VerifyIntegrity());
    }
}
