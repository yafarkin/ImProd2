using Game.Config.Catalog;

namespace Game.Engine;

/// <summary>
/// Остаточная стоимость построенной фабрики с учётом её текущего состояния (Блок 7.2, SPEC §5.11,
/// rebalance/2-sector-stepwise, 2026-08-23) — от <see
/// cref="FactoryDefinitionConfig.LiquidationValueCoefficient"/> (пол, полностью убитая фабрика,
/// <c>Condition=0</c>) линейно вверх до полной <see cref="FactoryDefinitionConfig.BuildCost"/>
/// (только что построена или отремонтирована, <c>Condition=1</c>). Раньше <see
/// cref="FinalScoreCalculator"/>/<see cref="IdealHallCalculator"/>/<see
/// cref="GameSession.SellFactory"/> считали плоскую долю (<c>BuildCost *
/// LiquidationValueCoefficient</c>) независимо от состояния — свежепостроенная и убитая ремонтом
/// фабрика стоили одинаково, что не создавало никакого стимула следить за износом. Общая формула для
/// всех трёх мест, собрана здесь, чтобы не разъезжались при следующей правке.
/// </summary>
public static class FactoryResidualValueCalculator
{
    public static decimal Calculate(FactoryDefinitionConfig definition, decimal condition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var residualFraction = definition.LiquidationValueCoefficient
                                + (1m - definition.LiquidationValueCoefficient) * condition;
        return definition.BuildCost * residualFraction;
    }
}
