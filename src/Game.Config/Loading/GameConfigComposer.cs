using Game.Config.Economy;
using Game.Config.ProductionModel;
using Game.Config.Session;

namespace Game.Config.Loading;

/// <summary>
/// Соединяет производственную модель и сессионные параметры в один <see cref="GameConfig"/> —
/// единственное место, где эти два авторских файла становятся тем плоским деревом, которое видит
/// весь остальной код (движок, резолвер, валидатор, хеш не знают о разрезе модель/сессия вообще).
/// Разрез существует только для авторов конфига: выбрать одну из нескольких производственных
/// моделей и один из нескольких сессионных наборов независимо друг от друга, не дублируя каталог
/// под каждую комбинацию длительности/сложности. После сборки, до валидации, применяет <see
/// cref="DifficultyScaler"/> — так уже собранный <see cref="GameConfig"/> (например, из <c>Samples/gameconfig.*.json</c>,
/// минующий этот путь) слайдер сложности не трогает вовсе.
/// </summary>
public static class GameConfigComposer
{
    /// <summary>Собирает <see cref="GameConfig"/> из модели и сессионных параметров.</summary>
    public static GameConfig Compose(ProductionModelConfig productionModel, SessionConfig session)
    {
        ArgumentNullException.ThrowIfNull(productionModel);
        ArgumentNullException.ThrowIfNull(session);

        var composed = new GameConfig
        {
            Sectors = productionModel.Sectors,
            Materials = productionModel.Materials,
            Recipes = productionModel.Recipes,
            FactoryDefinitions = productionModel.FactoryDefinitions,
            GenerationResearch = productionModel.GenerationResearch,
            StartingConditions = session.StartingConditions,
            SessionPresets = session.SessionPresets,
            PhaseTiming = session.PhaseTiming,
            Economy = new EconomyConfig
            {
                EmergencyPurchaseBaseMultiplier = session.Economy.EmergencyPurchaseBaseMultiplier,
                EmergencyPurchasePressureMultiplierPerUnit = session.Economy.EmergencyPurchasePressureMultiplierPerUnit,
                EmergencyPurchasePressureHalfLifeTurns = session.Economy.EmergencyPurchasePressureHalfLifeTurns,
                BaseMarketPerMaterial = productionModel.BaseMarketPerMaterial,
                MarginMultiplierByProcessingLevel = session.Economy.MarginMultiplierByProcessingLevel,
                MarketCapacityOverflowDiscount = session.Economy.MarketCapacityOverflowDiscount,
                ElectricityBasePrice = session.Economy.ElectricityBasePrice,
                ElectricityConsumptionPerOutputUnit = session.Economy.ElectricityConsumptionPerOutputUnit,
                TrendScenario = session.Economy.TrendScenario,
                WarehouseLiquidationRate = session.Economy.WarehouseLiquidationRate,
            },
            WorkerProductivity = session.WorkerProductivity,
            Rnd = session.Rnd,
            Wear = session.Wear,
            Warehouse = session.Warehouse,
            Reputation = session.Reputation,
            Contracts = session.Contracts,
            Taxes = session.Taxes,
            Deposits = session.Deposits,
            News = session.News,
            FeatureFlags = session.FeatureFlags,
        };

        return DifficultyScaler.Apply(composed, session.DifficultyLevel);
    }
}
