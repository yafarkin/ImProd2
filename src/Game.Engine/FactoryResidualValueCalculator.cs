using Game.Config.Catalog;

namespace Game.Engine;

/// <summary>
/// Остаточная стоимость построенной фабрики с учётом её текущего состояния (Блок 7.2, SPEC §5.11,
/// rebalance/2-sector-stepwise, 2026-08-23) — от <see
/// cref="FactoryDefinitionConfig.LiquidationValueCoefficient"/> (пол, полностью убитая фабрика,
/// <c>Condition=0</c>) линейно вверх до полной <see cref="FactoryDefinitionConfig.BuildCost"/>
/// (только что построена или отремонтирована, <c>Condition=1</c>). Общая формула для <see
/// cref="FinalScoreCalculator"/>, <see cref="IdealHallCalculator"/> и <see
/// cref="GameSession.SellFactory"/> — раньше была продублирована в первых двух, здесь собрана в
/// одном месте, чтобы не разъезжались при следующей правке.
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
