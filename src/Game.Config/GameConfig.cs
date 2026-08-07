using Game.Config.Catalog;
using Game.Config.Contracts;
using Game.Config.Economy;
using Game.Config.News;
using Game.Config.Session;

namespace Game.Config;

/// <summary>
/// Корень конфигурации игровой сессии: вся игровая логика — данные, а не код (AGENTS §3).
/// Загружается из JSON один раз на сессию; ссылки между разделами — по строковым кодам,
/// ссылочная целостность проверяется загрузчиком (Блок 2.2), не этим типом.
/// </summary>
public sealed record GameConfig
{
    /// <summary>Секторы экономики.</summary>
    public required IReadOnlyList<SectorConfig> Sectors { get; init; }

    /// <summary>Справочник материалов.</summary>
    public required IReadOnlyList<MaterialConfig> Materials { get; init; }

    /// <summary>Справочник рецептов.</summary>
    public required IReadOnlyList<RecipeConfig> Recipes { get; init; }

    /// <summary>Справочник типов фабрик.</summary>
    public required IReadOnlyList<FactoryDefinitionConfig> FactoryDefinitions { get; init; }

    /// <summary>Стартовые условия команды.</summary>
    public required StartingConditionsConfig StartingConditions { get; init; }

    /// <summary>Доступные пресеты длительности сессии.</summary>
    public required IReadOnlyList<SessionPresetConfig> SessionPresets { get; init; }

    /// <summary>Длительности фаз хода.</summary>
    public required PhaseTimingConfig PhaseTiming { get; init; }

    /// <summary>Параметры внешней экономики и рынка.</summary>
    public required EconomyConfig Economy { get; init; }

    /// <summary>Параметры производительности от числа рабочих.</summary>
    public required WorkerProductivityConfig WorkerProductivity { get; init; }

    /// <summary>Параметры R&amp;D — стоимость перехода фабрики на следующий уровень и его эффект.</summary>
    public required RndConfig Rnd { get; init; }

    /// <summary>Параметры износа и капремонта фабрик — см. doc-comment <see cref="WearConfig"/>.</summary>
    public required WearConfig Wear { get; init; }

    /// <summary>Параметры командного исследования, разблокирующего доступ к более глубоким переделам пирамиды.</summary>
    public required GenerationResearchConfig GenerationResearch { get; init; }

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
