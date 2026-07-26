using Game.Config.Economy;
using Game.Config.Session;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Финансовая часть расчёта тика (Блок 4.3; SPEC §4 — «финансы» идут первым шагом расчёта):
/// проценты по долгу → зарплаты → принудительный кредит, если после этого баланс всё ещё в минусе,
/// в этом фиксированном порядке. Возвращает готовые события, но не применяет их — вызывающий код
/// (тесты сейчас, оркестровка полного тика в Блоке 4.4) сам решает, куда и как их дописать в
/// журнал, как и <see cref="ProductionCalculator"/> в Блоке 4.2.
/// </summary>
public static class TickFinanceStep
{
    /// <summary>(Опц.) налоги и депозиты (SPEC §5.9-§5.10) в этот шаг не входят — сознательно отложены, см. AGENTS-память.</summary>
    public static IReadOnlyList<Change<GameSessionState>> Run(
        Team team, StartingConditionsConfig loanConfig, WorkerProductivityConfig workerConfig)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(loanConfig);
        ArgumentNullException.ThrowIfNull(workerConfig);

        var changes = new List<Change<GameSessionState>>();
        var projectedBalance = team.Balance;

        var interest = FinanceCalculator.CalculateInterest(team, loanConfig);
        if (interest > 0)
        {
            changes.Add(new LoanInterestCharged
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                Amount = interest,
                Rate = FinanceCalculator.CalculateEffectiveLoanRate(team, loanConfig),
            });
            projectedBalance -= interest;
        }

        var totalWorkers = team.Factories.Sum(factory => factory.Workers);
        var salaries = FinanceCalculator.CalculateSalaries(totalWorkers, workerConfig);
        if (salaries > 0)
        {
            changes.Add(new SalariesPaid { Id = Ulid.NewUlid(), TeamId = team.Id, TotalWorkers = totalWorkers, Amount = salaries });
            projectedBalance -= salaries;
        }

        if (projectedBalance < 0)
        {
            changes.Add(new ForcedLoanTaken
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                Amount = -projectedBalance,
                NewPenaltyRateSurcharge = team.PenaltyRateSurcharge + loanConfig.ForcedLoanPenaltyRatePerOccurrence,
            });
        }

        return changes;
    }
}
