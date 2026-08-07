using Game.Config.Catalog;
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

        return CalculateEffectiveLoanRate(team.Debt, team.PenaltyRateSurcharge, reputationPercentage, loanConfig);
    }

    /// <summary>
    /// То же самое на сырых числах, а не на живой команде (Блок 9.2) — нужно для предпросмотра
    /// ставки/платежа гипотетического займа до подтверждения (SPEC §5.9: «в UI до подтверждения —
    /// расчёт платежа за ход»), где долг после займа ещё не применён ни к какой реальной команде.
    /// </summary>
    public static decimal CalculateEffectiveLoanRate(
        decimal debt, decimal penaltyRateSurcharge, decimal reputationPercentage, StartingConditionsConfig loanConfig)
    {
        ArgumentNullException.ThrowIfNull(loanConfig);

        var reputationPenalty = loanConfig.MaxReputationRatePenalty * (100m - reputationPercentage) / 100m;

        return loanConfig.BaseLoanInterestRate
               + loanConfig.LoanInterestRateGrowthPerUnitBorrowed * debt
               + penaltyRateSurcharge
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

    /// <summary>
    /// Обязательный платёж по телу долга за один ход — доля от текущего долга
    /// (<see cref="StartingConditionsConfig.MandatoryRepaymentRatePerTurn"/>), отдельно от процентов
    /// (см. <see cref="CalculateInterest"/>, тело они не уменьшают); 0, если долга нет. Процент от
    /// долга по определению никогда не доходит ровно до нуля — геометрическая убыль. Как только сам
    /// такой платёж опускается ниже одной денежной единицы (уже неотличимо от нуля на экране, где
    /// суммы округляются до целых), это перестаёт быть содержательным решением — вместо вечных
    /// исчезающе малых списаний и записей «−0 ¤» в истории операций долг в этот момент закрывается
    /// целиком одним платежом (запрос пользователя).
    /// </summary>
    public static decimal CalculateMandatoryRepayment(Team team, StartingConditionsConfig loanConfig)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(loanConfig);

        if (team.Debt <= 0)
        {
            return 0m;
        }

        var repayment = team.Debt * loanConfig.MandatoryRepaymentRatePerTurn;
        return repayment is > 0m and < 1m ? team.Debt : repayment;
    }

    /// <summary>
    /// Суммарная зарплата за один ход для заданной ОБЩЕЙ численности рабочих команды (сумма по всем
    /// её фабрикам, не одной фабрики) — линейно до <see cref="WorkerProductivityConfig.TeamSalaryBaseWorkerCount"/>,
    /// дальше рабочие сверх порога обходятся дороже базовой ставки в <see cref="WorkerProductivityConfig.SalaryEscalationFactor"/>
    /// раз (зеркало убывающей отдачи выработки, <see cref="ProductionCalculator"/>, но для стоимости,
    /// а не выработки — запрос пользователя: раздувать одну фабрику должно становиться дороже само
    /// по себе, без штрафов за неудачу).
    /// </summary>
    public static decimal CalculateSalaries(int totalWorkers, WorkerProductivityConfig productivity)
    {
        ArgumentNullException.ThrowIfNull(productivity);

        if (totalWorkers <= productivity.TeamSalaryBaseWorkerCount)
        {
            return totalWorkers * productivity.SalaryPerWorkerPerTurn;
        }

        var baseCost = productivity.TeamSalaryBaseWorkerCount * productivity.SalaryPerWorkerPerTurn;
        var excessWorkers = totalWorkers - productivity.TeamSalaryBaseWorkerCount;
        var excessCost = excessWorkers * productivity.SalaryPerWorkerPerTurn * productivity.SalaryEscalationFactor;
        return baseCost + excessCost;
    }

    /// <summary>
    /// Суммарные капитальные затраты за один ход по всем построенным фабрикам команды
    /// (<see cref="FactoryDefinitionConfig.FixedCostPerTurn"/>) — платится за каждую построенную
    /// фабрику вне зависимости от числа рабочих и объёма выпуска (запрос пользователя: «платим за
    /// фабрику, даже если она вообще не работает»).
    /// </summary>
    public static decimal CalculateFactoryUpkeep(
        IReadOnlyList<Factory> factories, IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions, WearConfig wearConfig)
    {
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(factoryDefinitions);
        ArgumentNullException.ThrowIfNull(wearConfig);

        // Фабрики на вынужденном простое исключены отсюда: их содержание списывается отдельно, по
        // льготному тарифу простоя (см. WearStep/FactoryRepairTurnPassed) — второй штраф поверх был бы
        // избыточен, фабрика и так уже наказана простоем.
        return factories
            .Where(factory => !factory.IsUnderRepair)
            .Sum(factory => factoryDefinitions.First(definition => definition.Id == factory.Definition.Id).FixedCostPerTurn
                             * WearCalculator.CalculateUpkeepPenaltyMultiplier(factory.Condition, wearConfig));
    }
}
