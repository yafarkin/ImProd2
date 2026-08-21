using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Чистые расчёты финансового шага тика (Блок 4.3): не трогает журнал и не мутирует состояние — так
/// же, как <see cref="ProductionCalculator"/> в Блоке 4.2.
/// </summary>
public static class FinanceCalculator
{
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
