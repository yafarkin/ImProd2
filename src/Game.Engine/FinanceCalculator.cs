using Game.Config.Economy;
using Game.Config.Session;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Чистые расчёты финансового шага тика (Блок 4.3, SPEC §5.9): не трогает журнал и не мутирует
/// состояние — так же, как <see cref="ProductionCalculator"/> в Блоке 4.2.
/// </summary>
public static class FinanceCalculator
{
    /// <summary>
    /// Эффективная ставка по текущему долгу команды: база + рост от размера долга + накопленная
    /// штрафная надбавка за прошлые принудительные займы (SPEC §5.9).
    /// </summary>
    public static decimal CalculateEffectiveLoanRate(Team team, StartingConditionsConfig loanConfig)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(loanConfig);

        return loanConfig.BaseLoanInterestRate
               + loanConfig.LoanInterestRateGrowthPerUnitBorrowed * team.Debt
               + team.PenaltyRateSurcharge;
    }

    /// <summary>Проценты по текущему долгу за один ход; 0, если долга нет.</summary>
    public static decimal CalculateInterest(Team team, StartingConditionsConfig loanConfig)
    {
        ArgumentNullException.ThrowIfNull(team);

        if (team.Debt <= 0)
        {
            return 0m;
        }

        return team.Debt * CalculateEffectiveLoanRate(team, loanConfig);
    }

    /// <summary>Суммарная зарплата за один ход для заданного числа рабочих.</summary>
    public static decimal CalculateSalaries(int totalWorkers, WorkerProductivityConfig productivity)
    {
        ArgumentNullException.ThrowIfNull(productivity);

        return totalWorkers * productivity.SalaryPerWorkerPerTurn;
    }
}
