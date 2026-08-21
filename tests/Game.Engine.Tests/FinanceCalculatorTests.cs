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
        // Выше числа рабочих в любом сценарии этого файла — большинство тестов не про
        // прогрессивную надбавку, у неё отдельный конфиг ниже (WorkerConfigWithSalaryEscalation).
        TeamSalaryBaseWorkerCount = 1000,
        SalaryEscalationFactor = 1.5m,
    };

    private static readonly WorkerProductivityConfig WorkerConfigWithSalaryEscalation = WorkerConfig with
    {
        TeamSalaryBaseWorkerCount = 5,
        SalaryEscalationFactor = 2m,
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

    [Fact]
    public void CalculateSalaries_Stays_Flat_At_Or_Below_The_Team_Base_Worker_Count()
    {
        // 5 <= TeamSalaryBaseWorkerCount(5) — как раньше, без надбавки.
        Assert.Equal(25m, FinanceCalculator.CalculateSalaries(totalWorkers: 5, WorkerConfigWithSalaryEscalation));
    }

    [Fact]
    public void CalculateSalaries_Charges_A_Higher_Rate_For_Workers_Beyond_The_Team_Base_Worker_Count()
    {
        // 5 по базовой ставке (5*5=25) + 3 сверх порога по ставке *2 (3*5*2=30) = 55.
        Assert.Equal(55m, FinanceCalculator.CalculateSalaries(totalWorkers: 8, WorkerConfigWithSalaryEscalation));
    }
}
