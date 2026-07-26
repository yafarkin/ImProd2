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
    /// штрафная надбавка за прошлые принудительные займы + надбавка за репутацию (SPEC §5.9:
    /// «ставка зависит от закредитованности и репутации», Блок 6.2) — линейно от 0 при 100%
    /// репутации до <see cref="StartingConditionsConfig.MaxReputationRatePenalty"/> при 0%.
    /// </summary>
    public static decimal CalculateEffectiveLoanRate(Team team, StartingConditionsConfig loanConfig, decimal reputationPercentage)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(loanConfig);

        var reputationPenalty = loanConfig.MaxReputationRatePenalty * (100m - reputationPercentage) / 100m;

        return loanConfig.BaseLoanInterestRate
               + loanConfig.LoanInterestRateGrowthPerUnitBorrowed * team.Debt
               + team.PenaltyRateSurcharge
               + reputationPenalty;
    }

    /// <summary>Проценты по текущему долгу за один ход; 0, если долга нет.</summary>
    public static decimal CalculateInterest(Team team, StartingConditionsConfig loanConfig, decimal reputationPercentage)
    {
        ArgumentNullException.ThrowIfNull(team);

        if (team.Debt <= 0)
        {
            return 0m;
        }

        return team.Debt * CalculateEffectiveLoanRate(team, loanConfig, reputationPercentage);
    }

    /// <summary>Суммарная зарплата за один ход для заданного числа рабочих.</summary>
    public static decimal CalculateSalaries(int totalWorkers, WorkerProductivityConfig productivity)
    {
        ArgumentNullException.ThrowIfNull(productivity);

        return totalWorkers * productivity.SalaryPerWorkerPerTurn;
    }
}
