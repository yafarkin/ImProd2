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
    /// её фабрикам, не одной фабрики) — плоско, <see cref="WorkerProductivityConfig.SalaryPerWorkerPerTurn"/>
    /// на человека. Раньше (rebalance/2-sector-stepwise, до 2026-08-23) здесь была ещё и командная
    /// прогрессия сверх порога (зеркало убывающей отдачи выработки, <see cref="ProductionCalculator"/>,
    /// но для стоимости) — убрана по запросу пользователя: с реалистичным числом фабрик в продакшн-
    /// модели (9 типов × 10 рабочих) порог оказался ниже, чем у любой нормально укомплектованной
    /// команды, так что прогрессия срабатывала всегда, ничего не различая, — плюс её не учитывала
    /// себестоимость (<see cref="MaterialCostCalculator"/> считает зарплату плоско, без прогрессии),
    /// то есть даже сработав, она не была ничем компенсирована. Убывающая отдача выработки по
    /// численности одной фабрики (<see cref="ProductionCalculator.CalculateEffectiveCapacity"/>) сама
    /// по себе уже создаёт трение против бездумного найма — второй, отдельно калибруемый рычаг ради
    /// того же эффекта был признан избыточным.
    /// </summary>
    public static decimal CalculateSalaries(int totalWorkers, WorkerProductivityConfig productivity)
    {
        ArgumentNullException.ThrowIfNull(productivity);

        return totalWorkers * productivity.SalaryPerWorkerPerTurn;
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
