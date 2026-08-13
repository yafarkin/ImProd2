using Game.Config.Catalog;
using Game.Config.ProductionModel;

namespace Game.Config.Loading;

/// <summary>
/// Проверяет ссылочную целостность конфига до попытки построить из него доменный граф: материалы
/// принадлежат существующим секторам, рецепты ссылаются на существующие материалы, у каждого
/// материала — включая сырьё уровня 0, которое добывается фабрикой-добытчиком, а не покупается у
/// системы — ровно один производитель (SPEC §5.2 — иначе он, включая флагманы, недостижим в
/// цепочке), рецепт сырья не имеет входов (добывается, а не строится из других материалов), фабрики
/// предлагают рецепты своего сектора, в графе рецептов нет циклов. Возвращает все найденные проблемы
/// разом, а не только первую.
///
/// Все эти проверки целиком лежат внутри производственной модели (<see cref="ProductionModelConfig"/>)
/// — ни одна не пересекает границу модель/сессия, поэтому <see cref="ValidateProductionModel"/>
/// доступен как самостоятельная точка входа (например, для проверки модели до выбора сессионных
/// параметров), а <see cref="Validate"/> для уже собранного <see cref="GameConfig"/> — тонкая
/// обёртка над той же логикой.
/// </summary>
public static class GameConfigValidator
{
    /// <summary>Проверяет уже собранный <see cref="GameConfig"/> (используется <see cref="GameConfigLoader"/>).</summary>
    public static IReadOnlyList<string> Validate(GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return ValidateCore(config.Sectors, config.Materials, config.Recipes, config.FactoryDefinitions);
    }

    /// <summary>Проверяет производственную модель саму по себе, независимо от сессионных параметров.</summary>
    public static IReadOnlyList<string> ValidateProductionModel(ProductionModelConfig productionModel)
    {
        ArgumentNullException.ThrowIfNull(productionModel);

        return ValidateCore(
            productionModel.Sectors, productionModel.Materials, productionModel.Recipes, productionModel.FactoryDefinitions);
    }

    private static IReadOnlyList<string> ValidateCore(
        IReadOnlyList<SectorConfig> sectors,
        IReadOnlyList<MaterialConfig> materials,
        IReadOnlyList<RecipeConfig> recipes,
        IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions)
    {
        var errors = new List<string>();

        var sectorIds = CollectUniqueIds(sectors.Select(sector => sector.Id), "Sector", errors);
        var materialIds = CollectUniqueIds(materials.Select(material => material.Id), "Material", errors);
        var recipeIds = CollectUniqueIds(recipes.Select(recipe => recipe.Id), "Recipe", errors);
        CollectUniqueIds(factoryDefinitions.Select(factory => factory.Id), "FactoryDefinition", errors);

        foreach (var material in materials)
        {
            if (!sectorIds.Contains(material.SectorId))
            {
                errors.Add($"Material '{material.Id}' references unknown sector '{material.SectorId}'.");
            }
        }

        var producersByMaterialId = ValidateRecipes(materials, recipes, materialIds, errors);
        ValidateProducerCardinality(materials, producersByMaterialId, errors);
        ValidateFactoryDefinitions(factoryDefinitions, recipes, materials, sectorIds, recipeIds, errors);

        // Поиск циклов предполагает, что все ссылки уже разрешимы; на битом графе он выдал бы
        // запутанные побочные ошибки поверх настоящей проблемы.
        if (errors.Count == 0)
        {
            DetectCycles(materials, recipes, errors);
        }

        return errors;
    }

    private static Dictionary<string, List<string>> ValidateRecipes(
        IReadOnlyList<MaterialConfig> materials, IReadOnlyList<RecipeConfig> recipes, HashSet<string> materialIds, List<string> errors)
    {
        var producersByMaterialId = new Dictionary<string, List<string>>();

        foreach (var recipe in recipes)
        {
            if (!materialIds.Contains(recipe.OutputMaterialId))
            {
                errors.Add($"Recipe '{recipe.Id}' produces unknown material '{recipe.OutputMaterialId}'.");
            }
            else
            {
                var outputMaterial = materials.First(material => material.Id == recipe.OutputMaterialId);
                if (outputMaterial.Level == 0 && recipe.Inputs.Count > 0)
                {
                    errors.Add(
                        $"Recipe '{recipe.Id}' produces raw material '{outputMaterial.Id}' (level 0); " +
                        "raw materials are mined, not built from other materials, so their recipe must have no inputs.");
                }
                if (outputMaterial.Level > 0 && recipe.Inputs.Count == 0)
                {
                    errors.Add($"Recipe '{recipe.Id}' has no inputs.");
                }

                if (!producersByMaterialId.TryGetValue(recipe.OutputMaterialId, out var producers))
                {
                    producers = new List<string>();
                    producersByMaterialId[recipe.OutputMaterialId] = producers;
                }

                producers.Add(recipe.Id);
            }

            foreach (var input in recipe.Inputs)
            {
                if (!materialIds.Contains(input.MaterialId))
                {
                    errors.Add($"Recipe '{recipe.Id}' consumes unknown material '{input.MaterialId}'.");
                }
            }

            if (recipe.Inputs.Any(input => input.MaterialId == recipe.OutputMaterialId))
            {
                errors.Add($"Recipe '{recipe.Id}' has its own output '{recipe.OutputMaterialId}' among its inputs.");
            }
        }

        return producersByMaterialId;
    }

    private static void ValidateProducerCardinality(
        IReadOnlyList<MaterialConfig> materials, Dictionary<string, List<string>> producersByMaterialId, List<string> errors)
    {
        foreach (var material in materials)
        {
            if (!producersByMaterialId.TryGetValue(material.Id, out var producers))
            {
                errors.Add(
                    $"Material '{material.Id}' (level {material.Level}) has no recipe producing it; " +
                    "it is unreachable in the production chain.");

                continue;
            }

            if (producers.Count > 1)
            {
                errors.Add(
                    $"Material '{material.Id}' is produced by multiple recipes ({string.Join(", ", producers)}); " +
                    "each material must have exactly one producer (SPEC §5.2).");
            }
        }
    }

    private static void ValidateFactoryDefinitions(
        IReadOnlyList<FactoryDefinitionConfig> factoryDefinitions,
        IReadOnlyList<RecipeConfig> recipes,
        IReadOnlyList<MaterialConfig> materials,
        HashSet<string> sectorIds,
        HashSet<string> recipeIds,
        List<string> errors)
    {
        foreach (var factory in factoryDefinitions)
        {
            if (!sectorIds.Contains(factory.SectorId))
            {
                errors.Add($"FactoryDefinition '{factory.Id}' references unknown sector '{factory.SectorId}'.");
            }

            if (factory.RecipeIds.Count == 0)
            {
                errors.Add($"FactoryDefinition '{factory.Id}' offers no recipes.");
            }

            foreach (var recipeId in factory.RecipeIds)
            {
                if (!recipeIds.Contains(recipeId))
                {
                    errors.Add($"FactoryDefinition '{factory.Id}' references unknown recipe '{recipeId}'.");
                    continue;
                }

                var recipe = recipes.First(candidate => candidate.Id == recipeId);
                var outputMaterial = materials.FirstOrDefault(material => material.Id == recipe.OutputMaterialId);
                if (outputMaterial is not null && outputMaterial.SectorId != factory.SectorId)
                {
                    errors.Add(
                        $"FactoryDefinition '{factory.Id}' (sector '{factory.SectorId}') offers recipe '{recipeId}', " +
                        $"which produces material '{outputMaterial.Id}' belonging to sector '{outputMaterial.SectorId}'.");
                }
            }
        }
    }

    private static HashSet<string> CollectUniqueIds(IEnumerable<string> ids, string kind, List<string> errors)
    {
        var seen = new HashSet<string>();
        foreach (var id in ids)
        {
            if (!seen.Add(id))
            {
                errors.Add($"Duplicate {kind} id '{id}'.");
            }
        }

        return seen;
    }

    private static void DetectCycles(IReadOnlyList<MaterialConfig> materials, IReadOnlyList<RecipeConfig> recipes, List<string> errors)
    {
        var recipeByOutputMaterialId = recipes.ToDictionary(recipe => recipe.OutputMaterialId);
        var state = new Dictionary<string, int>();
        var path = new List<string>();

        // Перебираем в порядке объявления в JSON (не в порядке словаря/hashset), чтобы вывод ошибок
        // был детерминирован (AGENTS §2, правило 6), хотя это влияет лишь на порядок сообщений.
        foreach (var material in materials)
        {
            if (!state.ContainsKey(material.Id))
            {
                VisitForCycle(material.Id, recipeByOutputMaterialId, state, path, errors);
            }
        }
    }

    private static void VisitForCycle(
        string materialId,
        IReadOnlyDictionary<string, RecipeConfig> recipeByOutputMaterialId,
        Dictionary<string, int> state,
        List<string> path,
        List<string> errors)
    {
        const int InProgress = 1;
        const int Done = 2;

        if (state.TryGetValue(materialId, out var status))
        {
            if (status == InProgress)
            {
                var cycleStart = path.IndexOf(materialId);
                var cycle = path.Skip(cycleStart).Append(materialId);
                errors.Add($"Circular production dependency: {string.Join(" -> ", cycle)}.");
            }

            return;
        }

        if (!recipeByOutputMaterialId.TryGetValue(materialId, out var recipe))
        {
            state[materialId] = Done;
            return;
        }

        state[materialId] = InProgress;
        path.Add(materialId);

        foreach (var input in recipe.Inputs)
        {
            VisitForCycle(input.MaterialId, recipeByOutputMaterialId, state, path, errors);
        }

        path.RemoveAt(path.Count - 1);
        state[materialId] = Done;
    }
}
