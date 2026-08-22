namespace Game.Config.Economy;

/// <summary>
/// Параметры внешней экономики сессии (SPEC §5.3-5.5): аварийная закупка, понижающий коэффициент за
/// превышение ёмкости рынка, электричество, сценарный тренд. Все числа — заглушки, требуют
/// калибровки, кроме двух зафиксированных пользователем правил («окно маркетмейкера», запрос
/// пользователя, rebalance/2-sector-stepwise, 2026-08-22, поднято тем же днём 5%/30% → 10%/40% →
/// 20%/40%): продажа системе — везде <c>Game.Engine.MarketSaleCalculator.SystemSaleMarginMultiplier</c>
/// (себестоимость + 20%, без деления по уровню передела — до этого была таблица
/// <c>MarginMultiplierByProcessingLevel</c>, убрана как класс механики целиком, не только числа:
/// показала себя источником багов — асимметрия множителей между уровнями почти обнуляла маржу
/// передела, дважды пришлось чинить вручную, step8 и step12); аварийная закупка — база <see
/// cref="EmergencyPurchaseBaseMultiplier"/> зафиксирована на 1.4 (себестоимость + 40%) на нейтральном
/// (3) уровне ползунка сложности, поле осталось конфигурируемым (не константа в коде, в отличие от
/// продажи системе) — это один из откалиброванных рычагов <see cref="DifficultyScaler"/>, ломать
/// которую не входило в задачу.
/// </summary>
public sealed record EconomyConfig
{
    /// <summary>
    /// Множитель к себестоимости материала при аварийной закупке у системы, если команда давно (или
    /// никогда) не закупала этот материал экстренно — было <c>EmergencyPurchasePriceMultiplier</c>.
    /// Наказывает не саму операцию, а зависимость от неё (запрос пользователя): реальный множитель
    /// растёт сверх этой базы с недавним объёмом закупок именно этой команды именно этого материала,
    /// см. <see cref="EmergencyPurchasePressureMultiplierPerUnit"/>. Зафиксирован на 1.4 (см.
    /// doc-comment класса) во всех сессионных файлах на нейтральном уровне сложности — но поле не
    /// удалено из конфига (в отличие от <c>MarginMultiplierByProcessingLevel</c>), потому что
    /// <see cref="DifficultyScaler"/> его масштабирует.
    /// </summary>
    public required decimal EmergencyPurchaseBaseMultiplier { get; init; }

    /// <summary>
    /// Дополнительный множитель за каждую единицу «недавнего давления» — см.
    /// <c>EmergencyPurchasePressureCalculator</c>: чем больше команда закупала этот материал
    /// экстренно в последних ходах, тем дороже ей обходится следующая такая закупка того же
    /// материала. Заглушка, требует калибровки.
    /// </summary>
    public required decimal EmergencyPurchasePressureMultiplierPerUnit { get; init; }

    /// <summary>
    /// Период полураспада «давления» недавних экстренных закупок, в ходах — тот же приём, что
    /// <see cref="ReputationConfig.HalfLifeTurns"/>: несколько ходов без закупок этого материала —
    /// и цена вновь возвращается к базовой.
    /// </summary>
    public required int EmergencyPurchasePressureHalfLifeTurns { get; init; }

    /// <summary>Базовые цена и ёмкость по каждому материалу (Блок 6.1), от которых тренд отсчитывает изменение по ходам.</summary>
    public required IReadOnlyList<MaterialMarketConfig> BaseMarketPerMaterial { get; init; }

    /// <summary>Понижающий коэффициент цены при продаже сверх ёмкости рынка (0..1).</summary>
    public required decimal MarketCapacityOverflowDiscount { get; init; }

    /// <summary>Базовая цена электричества.</summary>
    public required decimal ElectricityBasePrice { get; init; }

    /// <summary>
    /// Расход электричества на единицу выпуска — вместе с текущей <see
    /// cref="Game.Domain.Market.ElectricityPrice"/> даёт переменную часть затрат фабрики на работу
    /// (энергия, растёт вместе с объёмом выпуска, в отличие от фиксированной
    /// <see cref="Game.Config.Catalog.FactoryDefinitionConfig.FixedCostPerTurn"/> — запрос
    /// пользователя: «если фабрика работает — рост затрат пропорционален»). Единая ставка для всех
    /// типов фабрик и уровней передела — не про то, что производит фабрика, а про сам факт работы.
    /// Заглушка, требует калибровки.
    /// </summary>
    public required decimal ElectricityConsumptionPerOutputUnit { get; init; }

    /// <summary>Сценарный тренд сессии, разбитый на отрезки ходов.</summary>
    public required IReadOnlyList<EconomyTrendPhaseConfig> TrendScenario { get; init; }

    /// <summary>
    /// Доля от текущей рыночной цены, по которой склад оценивается в итоговом счёте (SPEC §5.11:
    /// «≈50% рыночной цены»), 0..1. Заглушка, требует калибровки.
    /// </summary>
    public required decimal WarehouseLiquidationRate { get; init; }
}
