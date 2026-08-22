using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Итоговый счёт команды по остаточной стоимости (Блок 7.2, SPEC §5.11, доработано rebalance/2-sector-stepwise,
/// 2026-08-23 — запрос пользователя): «в любой момент считаем, сколько вы стоите при ликвидации» —
/// склад по доле от себестоимости (<see cref="MaterialCostCalculator"/>, не рыночной котировки),
/// фабрики — по остаточной стоимости постройки, привязанной к реальному состоянию (<see
/// cref="Factory.Condition"/>): от <see cref="FactoryDefinitionConfig.LiquidationValueCoefficient"/>
/// (пол, полностью убитая фабрика) линейно вверх до полной <see cref="FactoryDefinitionConfig.BuildCost"/>
/// (только что построена или отремонтирована, <c>Condition=1</c>) — было плоской долей независимо от
/// состояния, из-за чего только что отремонтированная и почти убитая фабрика стоили в счёте одинаково.
/// Чистый учёт, не движение денег — не создаёт стимула «нагенерировать себе выручку», в отличие от
/// наценки/себестоимости (см. `docs/rebalance-2sector/README.md`, разбор «аренда vs себестоимость»).
/// R&amp;D не учитывается (расход, ценность уже проявилась в производстве, а не в отдельном учёте).
/// Чистая функция — не мутирует ни команду, ни себестоимости; можно звать в любой момент сессии, не
/// только по её завершении.
/// </summary>
public static class FinalScoreCalculator
{
    public static FinalScoreResult Calculate(
        Team team, IReadOnlyDictionary<string, decimal> materialCosts, EconomyConfig economy,
        IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(materialCosts);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(factoryDefinitions);

        var warehouseValue = team.Warehouse.Stock.Sum(
            stock => stock.Quantity * materialCosts.GetValueOrDefault(stock.Material.Id, 0m) * economy.WarehouseLiquidationRate);

        var factoriesValue = team.Factories.Sum(factory =>
        {
            var definition = factoryDefinitions.First(f => f.Id == factory.Definition.Id);
            var residualFraction = definition.LiquidationValueCoefficient
                                    + (1m - definition.LiquidationValueCoefficient) * factory.Condition;
            return definition.BuildCost * residualFraction;
        });

        return new FinalScoreResult
        {
            TeamId = team.Id,
            Cash = team.Balance,
            WarehouseValue = warehouseValue,
            FactoriesValue = factoriesValue,
            Score = team.Balance + warehouseValue + factoriesValue,
        };
    }
}
