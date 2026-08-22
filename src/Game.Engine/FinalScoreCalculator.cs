using Game.Config.Catalog;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Итоговый счёт команды по остаточной стоимости (Блок 7.2, SPEC §5.11, доработано rebalance/2-sector-stepwise,
/// 2026-08-23 — запрос пользователя): «в любой момент считаем, сколько вы стоите при ликвидации» —
/// склад **ровно по себестоимости** (<see cref="MaterialCostCalculator"/>, без скидки на ликвидацию,
/// <c>EconomyConfig.WarehouseLiquidationRate</c> в этой формуле больше не участвует — было `× 0.5`,
/// сознательное упрощение по запросу пользователя: одна и та же формула для бота и для реальной игры,
/// без лишнего рычага, который надо было бы объяснять и калибровать отдельно), фабрики — по остаточной
/// стоимости постройки, привязанной к реальному состоянию (<see cref="Factory.Condition"/>): от <see
/// cref="FactoryDefinitionConfig.LiquidationValueCoefficient"/> (пол, полностью убитая фабрика)
/// линейно вверх до полной <see cref="FactoryDefinitionConfig.BuildCost"/> (только что построена или
/// отремонтирована, <c>Condition=1</c>). Чистый учёт, не движение денег — не создаёт стимула
/// «нагенерировать себе выручку», в отличие от наценки/себестоимости (см.
/// `docs/rebalance-2sector/README.md`, разбор «аренда vs себестоимость»). R&amp;D не учитывается
/// (расход, ценность уже проявилась в производстве, а не в отдельном учёте). Чистая функция — не
/// мутирует ни команду, ни себестоимости; можно звать в любой момент сессии, не только по её
/// завершении.
/// </summary>
public static class FinalScoreCalculator
{
    public static FinalScoreResult Calculate(
        Team team, IReadOnlyDictionary<string, decimal> materialCosts,
        IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(materialCosts);
        ArgumentNullException.ThrowIfNull(factoryDefinitions);

        var warehouseValue = team.Warehouse.Stock.Sum(
            stock => stock.Quantity * materialCosts.GetValueOrDefault(stock.Material.Id, 0m));

        var factoriesValue = team.Factories.Sum(factory =>
        {
            var definition = factoryDefinitions.First(f => f.Id == factory.Definition.Id);
            return FactoryResidualValueCalculator.Calculate(definition, factory.Condition);
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
