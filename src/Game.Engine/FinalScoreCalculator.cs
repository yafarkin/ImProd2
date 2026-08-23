using Game.Config.Catalog;
using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Итоговый счёт команды по ликвидационной стоимости (Блок 7.2, SPEC §5.11): «в конце считаем,
/// сколько вы стоите при ликвидации» — склад по доле от текущей рыночной цены, фабрики — по
/// остаточной стоимости постройки, привязанной к реальному состоянию (2026-08-23, см. <see
/// cref="FactoryResidualValueCalculator"/>: от <see
/// cref="FactoryDefinitionConfig.LiquidationValueCoefficient"/>, пол при полностью убитой фабрике,
/// линейно вверх до полной <see cref="FactoryDefinitionConfig.BuildCost"/> при <c>Condition=1</c>);
/// R&amp;D не учитывается (расход, ценность уже проявилась в производстве, а не в отдельном учёте).
/// Чистая функция — не мутирует ни команду, ни рынок; можно звать в любой момент сессии, не только
/// по её завершении.
/// </summary>
public static class FinalScoreCalculator
{
    public static FinalScoreResult Calculate(
        Team team, Market market, EconomyConfig economy, IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(factoryDefinitions);

        var warehouseValue = team.Warehouse.Stock.Sum(
            stock => stock.Quantity * market.QuoteOf(stock.Material.Id).Price * economy.WarehouseLiquidationRate);

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
