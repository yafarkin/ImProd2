using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.News;

namespace Game.Config.Session;

/// <summary>
/// Параметры конкретной сессии поверх производственной модели (<see
/// cref="Game.Config.ProductionModel.ProductionModelConfig"/>) — длительность, тайминг фаз,
/// сложность, включённые механики: всё, что не завязано на конкретный состав
/// секторов/материалов/рецептов и потому может свободно комбинироваться с любой моделью. <see
/// cref="Game.Config.Loading.GameConfigComposer"/> соединяет их в один <see
/// cref="Game.Config.GameConfig"/>.
/// </summary>
public sealed record SessionConfig
{
    /// <summary>Стартовые условия команды.</summary>
    public required StartingConditionsConfig StartingConditions { get; init; }

    /// <summary>Доступные пресеты длительности сессии.</summary>
    public required IReadOnlyList<SessionPresetConfig> SessionPresets { get; init; }

    /// <summary>Длительности фаз хода.</summary>
    public required PhaseTimingConfig PhaseTiming { get; init; }

    /// <summary>Параметры внешней экономики и рынка (кроме базовых цен по материалам — те в модели).</summary>
    public required SessionEconomyConfig Economy { get; init; }

    /// <summary>Параметры производительности от числа рабочих.</summary>
    public required WorkerProductivityConfig WorkerProductivity { get; init; }

    /// <summary>Параметры R&amp;D — стоимость перехода фабрики на следующий уровень и его эффект.</summary>
    public required RndConfig Rnd { get; init; }

    /// <summary>Параметры износа и капремонта фабрик.</summary>
    public required WearConfig Wear { get; init; }

    /// <summary>Параметры склада.</summary>
    public required WarehouseConfig Warehouse { get; init; }

    /// <summary>Параметры репутации.</summary>
    public required ReputationConfig Reputation { get; init; }

    /// <summary>Параметры контрактов.</summary>
    public required ContractsConfig Contracts { get; init; }

    /// <summary>Параметры налогов (используются, если включены флагом).</summary>
    public required TaxesConfig Taxes { get; init; }

    /// <summary>Параметры депозитов (используются, если включены флагом).</summary>
    public required DepositsConfig Deposits { get; init; }

    /// <summary>Библиотека заголовков новостной ленты.</summary>
    public required IReadOnlyList<NewsItemConfig> News { get; init; }

    /// <summary>Флаги включения механик MVP.</summary>
    public required FeatureFlagsConfig FeatureFlags { get; init; }
}
