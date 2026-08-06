using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Config.Session;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Финансовая часть расчёта тика (Блок 4.3; SPEC §4 — «финансы» идут первым шагом расчёта):
/// проценты по долгу → обязательный платёж по телу долга → зарплаты → капитальные затраты на
/// фабрики → плата за склад → принудительный кредит, если после всего этого баланс всё ещё в
/// минусе, в этом фиксированном порядке. Возвращает готовые события, но не применяет их —
/// вызывающий код (тесты сейчас, оркестровка полного тика в Блоке 4.4) сам решает, куда и как их
/// дописать в журнал, как и <see cref="ProductionCalculator"/> в Блоке 4.2.
/// </summary>
public static class TickFinanceStep
{
    /// <summary>
    /// (Опц.) налоги и депозиты (SPEC §5.9-§5.10) в этот шаг не входят — сознательно отложены, см.
    /// AGENTS-память. Плата за превышение склада (Блок 9.2, SPEC §5.7) уже входит — считается по
    /// остатку склада на начало хода, тем же порядком, что проценты (по долгу) и зарплата (по числу
    /// рабочих). Переменная часть затрат на работу фабрик (энергия, растёт вместе с объёмом
    /// выпуска — см. <see cref="FactoryProduced.OverheadCost"/>) сюда не входит: этот шаг идёт до
    /// расчёта производства за ход, объём выпуска ещё не известен — списывается отдельно, вместе с
    /// самим производством. <paramref name="reputationPercentage"/> — репутация команды на момент
    /// начала этого хода (Блок 6.2), посчитанная вызывающим кодом по истории журнала <em>до</em>
    /// событий этого же тика: собственные поставки/срывы текущего хода ещё не должны влиять на его
    /// же ставку.
    /// </summary>
    public static IReadOnlyList<Change<GameSessionState>> Run(
        Team team, StartingConditionsConfig loanConfig, WorkerProductivityConfig workerConfig,
        WarehouseConfig warehouseConfig, IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions,
        decimal reputationPercentage)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(loanConfig);
        ArgumentNullException.ThrowIfNull(workerConfig);
        ArgumentNullException.ThrowIfNull(warehouseConfig);
        ArgumentNullException.ThrowIfNull(factoryDefinitions);

        var changes = new List<Change<GameSessionState>>();
        var projectedBalance = team.Balance;

        var interest = FinanceCalculator.CalculateInterest(team, loanConfig, reputationPercentage);
        if (interest > 0)
        {
            changes.Add(new LoanInterestCharged
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                Amount = interest,
                Rate = FinanceCalculator.CalculateEffectiveLoanRate(team, loanConfig, reputationPercentage),
            });
            projectedBalance -= interest;
        }

        var mandatoryRepayment = FinanceCalculator.CalculateMandatoryRepayment(team, loanConfig);
        if (mandatoryRepayment > 0)
        {
            changes.Add(new MandatoryLoanRepaymentCharged
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                Amount = mandatoryRepayment,
                Rate = loanConfig.MandatoryRepaymentRatePerTurn,
            });
            projectedBalance -= mandatoryRepayment;
        }

        var totalWorkers = team.Factories.Sum(factory => factory.Workers);
        var salaries = FinanceCalculator.CalculateSalaries(totalWorkers, workerConfig);
        if (salaries > 0)
        {
            changes.Add(new SalariesPaid { Id = Ulid.NewUlid(), TeamId = team.Id, TotalWorkers = totalWorkers, Amount = salaries });
            projectedBalance -= salaries;
        }

        var factoryUpkeep = FinanceCalculator.CalculateFactoryUpkeep(team.Factories, factoryDefinitions);
        if (factoryUpkeep > 0)
        {
            changes.Add(new FactoryUpkeepPaid { Id = Ulid.NewUlid(), TeamId = team.Id, FactoryCount = team.Factories.Count, Amount = factoryUpkeep });
            projectedBalance -= factoryUpkeep;
        }

        var totalStock = team.Warehouse.Stock.Sum(stock => stock.Quantity);
        var warehouseFee = WarehouseFeeCalculator.Calculate(totalStock, warehouseConfig);
        if (warehouseFee.Fee > 0)
        {
            changes.Add(new WarehouseFeeCharged
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                OverageQuantity = warehouseFee.OverageQuantity,
                Amount = warehouseFee.Fee,
            });
            projectedBalance -= warehouseFee.Fee;
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
