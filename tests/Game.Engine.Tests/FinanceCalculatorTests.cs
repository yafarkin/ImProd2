using Game.Config.Economy;

namespace Game.Engine.Tests;

public class FinanceCalculatorTests
{
    private static readonly WorkerProductivityConfig WorkerConfig = new()
    {
        BaseWorkerCount = 5,
        DiminishingReturnsFactor = 0.5m,
        HireCostPerWorker = 50m,
        FireCostPerWorker = 30m,
        SalaryPerWorkerPerTurn = 5m,
    };

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
