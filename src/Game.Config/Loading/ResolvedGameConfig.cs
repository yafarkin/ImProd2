using Game.Domain;

namespace Game.Config.Loading;

/// <summary>
/// Результат успешной загрузки GameConfig: исходные данные плюс каталог (секторы, материалы,
/// рецепты, типы фабрик), развёрнутый в объектный граф Game.Domain — по одному каноническому
/// экземпляру на сектор/материал/рецепт, как того требуют объекты графа конфигурации
/// (сравниваются по ссылке, см. Recipe/FactoryDefinition).
/// </summary>
public sealed class ResolvedGameConfig
{
    /// <summary>Исходные (невалидированные ссылочно) данные конфига, как они пришли из JSON.</summary>
    public GameConfig Raw { get; }

    /// <summary>
    /// Контент-хеш конфига (SHA-256 от канонической сериализации <see cref="Raw"/>). Журнал сессии
    /// записывает его в первую запись и сверяет при восстановлении — привязка лога к своему конфигу.
    /// </summary>
    public string ContentHash { get; }

    /// <summary>Секторы.</summary>
    public IReadOnlyList<Sector> Sectors { get; }

    /// <summary>Материалы по коду.</summary>
    public IReadOnlyDictionary<string, Material> Materials { get; }

    /// <summary>Справочник «материал → производящий его рецепт».</summary>
    public RecipeBook RecipeBook { get; }

    /// <summary>Типы фабрик.</summary>
    public IReadOnlyList<FactoryDefinition> FactoryDefinitions { get; }

    public ResolvedGameConfig(
        GameConfig raw,
        IReadOnlyList<Sector> sectors,
        IReadOnlyDictionary<string, Material> materials,
        RecipeBook recipeBook,
        IReadOnlyList<FactoryDefinition> factoryDefinitions)
    {
        Raw = raw;
        Sectors = sectors;
        Materials = materials;
        RecipeBook = recipeBook;
        FactoryDefinitions = factoryDefinitions;
        ContentHash = GameConfigHash.Compute(raw);
    }
}
