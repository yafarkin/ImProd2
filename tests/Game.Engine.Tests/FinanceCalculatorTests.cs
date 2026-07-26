using Game.Config.Economy;
using Game.Config.Session;
using Game.Domain;

namespace Game.Engine.Tests;

public class FinanceCalculatorTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");

    private static readonly StartingConditionsConfig LoanConfig = new()
    {
        MaxStartingLoanAmount = 10000m,
        BaseLoanInterestRate = 0.05m,
        LoanInterestRateGrowthPerUnitBorrowed = 0.0001m,
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

    [Fact]
    public void CalculateInterest_Is_Zero_When_Team_Has_No_Debt()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);

        Assert.Equal(0m, FinanceCalculator.CalculateInterest(team, LoanConfig));
    }

    [Fact]
    public void CalculateInterest_Uses_The_Base_Rate_Plus_Growth_By_Debt_Size()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(1000m);

        // ставка = 0.05 + 0.0001 * 1000 = 0.15; проценты = 1000 * 0.15 = 150
        Assert.Equal(0.15m, FinanceCalculator.CalculateEffectiveLoanRate(team, LoanConfig));
        Assert.Equal(150m, FinanceCalculator.CalculateInterest(team, LoanConfig));
    }

    [Fact]
    public void CalculateEffectiveLoanRate_Includes_The_Penalty_Rate_Surcharge()
    {
        var team = new Team(Ulid.NewUlid(), "Команда А1", SectorA);
        team.TakeLoan(1000m);
        team.IncreasePenaltyRateSurcharge(0.2m);

        Assert.Equal(0.35m, FinanceCalculator.CalculateEffectiveLoanRate(team, LoanConfig));
    }

    [Fact]
    public void CalculateSalaries_Multiplies_Worker_Count_By_The_Configured_Rate()
    {
        Assert.Equal(35m, FinanceCalculator.CalculateSalaries(totalWorkers: 7, WorkerConfig));
    }

    [Fact]
    public void CalculateSalaries_Is_Zero_For_No_Workers()
    {
        Assert.Equal(0m, FinanceCalculator.CalculateSalaries(totalWorkers: 0, WorkerConfig));
    }
}
