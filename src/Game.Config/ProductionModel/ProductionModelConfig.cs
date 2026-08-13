using Game.Config.Catalog;
using Game.Config.Economy;

namespace Game.Config.ProductionModel;

/// <summary>
/// Производственная модель: каталог секторов/материалов/рецептов/фабрик и то, что зависит от его
/// формы, — базовая цена/ёмкость по каждому материалу (<see cref="BaseMarketPerMaterial"/>,
/// привязана к конкретным <see cref="MaterialConfig.Id"/> модели) и командное исследование
/// поколений (<see cref="GenerationResearch"/>, число порогов зависит от глубины пирамиды переделов
/// этой модели). Не содержит ничего, что относится к темпу/сложности конкретной сессии, —
/// см. <see cref="Game.Config.Session.SessionConfig"/> и <see
/// cref="Game.Config.Loading.GameConfigComposer"/>, который соединяет их в один <see
/// cref="Game.Config.GameConfig"/>. Смысл разреза: одна и та же экономика (цепочки, рецепты, цены)
/// может разыгрываться под разными сессионными параметрами (длительность, тайминг, сложность) без
/// дублирования каталога — см. обсуждение, приведшее к разрезу (было: три файла-пресета с
/// побайтово идентичным каталогом, различавшиеся только длительностью и таймингом).
/// </summary>
public sealed record ProductionModelConfig
{
    /// <summary>Секторы экономики.</summary>
    public required IReadOnlyList<SectorConfig> Sectors { get; init; }

    /// <summary>Справочник материалов.</summary>
    public required IReadOnlyList<MaterialConfig> Materials { get; init; }

    /// <summary>Справочник рецептов.</summary>
    public required IReadOnlyList<RecipeConfig> Recipes { get; init; }

    /// <summary>Справочник типов фабрик.</summary>
    public required IReadOnlyList<FactoryDefinitionConfig> FactoryDefinitions { get; init; }

    /// <summary>Базовые цена и ёмкость по каждому материалу — см. doc-comment <see cref="MaterialMarketConfig"/>.</summary>
    public required IReadOnlyList<MaterialMarketConfig> BaseMarketPerMaterial { get; init; }

    /// <summary>Параметры командного исследования, разблокирующего доступ к более глубоким переделам пирамиды.</summary>
    public required GenerationResearchConfig GenerationResearch { get; init; }
}
