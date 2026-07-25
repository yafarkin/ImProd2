using Game.Config.Catalog;

namespace Game.Config.Loading;

/// <summary>
/// Проверяет ссылочную целостность GameConfig до попытки построить из него доменный граф:
/// материалы принадлежат существующим секторам, рецепты ссылаются на существующие материалы,
/// у каждого не-сырьевого материала ровно один производитель (SPEC §5.2 — иначе он, включая
/// флагманы, недостижим в цепочке), фабрики предлагают рецепты своего сектора, в графе рецептов
/// нет циклов. Возвращает все найденные проблемы разом, а не только первую.
/// </summary>
public static class GameConfigValidator
{
    public static IReadOnlyList<string> Validate(GameConfig config)
    {
        var errors = new List<string>();

        var sectorIds = CollectUniqueIds(config.Sectors.Select(sector => sector.Id), "Sector", errors);
        var materialIds = CollectUniqueIds(config.Materials.Select(material => material.Id), "Material", errors);
        var recipeIds = CollectUniqueIds(config.Recipes.Select(recipe => recipe.Id), "Recipe", errors);
        CollectUniqueIds(config.FactoryDefinitions.Select(factory => factory.Id), "FactoryDefinition", errors);

        foreach (var material in config.Materials)
        {
            if (!sectorIds.Contains(material.SectorId))
            {
                errors.Add($"Material '{material.Id}' references unknown sector '{material.SectorId}'.");
            }
        }

        var producersByMaterialId = ValidateRecipes(config, materialIds, errors);
        ValidateProducerCardinality(config, producersByMaterialId, errors);
        ValidateFactoryDefinitions(config, sectorIds, recipeIds, errors);

        // Cycle detection assumes every reference already resolves; running it over a broken
        // graph would produce confusing follow-on errors on top of the real problem.
        if (errors.Count == 0)
        {
            DetectCycles(config, errors);
        }

        return errors;
    }

    private static Dictionary<string, List<string>> ValidateRecipes(
        GameConfig config, HashSet<string> materialIds, List<string> errors)
    {
        var producersByMaterialId = new Dictionary<string, List<string>>();

        foreach (var recipe in config.Recipes)
        {
            if (!materialIds.Contains(recipe.OutputMaterialId))
            {
                errors.Add($"Recipe '{recipe.Id}' produces unknown material '{recipe.OutputMaterialId}'.");
            }
            else
            {
                var outputMaterial = config.Materials.First(material => material.Id == recipe.OutputMaterialId);
                if (outputMaterial.Level == 0)
                {
                    errors.Add(
                        $"Recipe '{recipe.Id}' produces raw material '{outputMaterial.Id}' (level 0); " +
                        "raw materials are bought from the system and must not have a recipe.");
                }

                if (!producersByMaterialId.TryGetValue(recipe.OutputMaterialId, out var producers))
                {
                    producers = new List<string>();
                    producersByMaterialId[recipe.OutputMaterialId] = producers;
                }

                producers.Add(recipe.Id);
            }

            if (recipe.Inputs.Count == 0)
            {
                errors.Add($"Recipe '{recipe.Id}' has no inputs.");
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
        GameConfig config, Dictionary<string, List<string>> producersByMaterialId, List<string> errors)
    {
        foreach (var material in config.Materials)
        {
            if (!producersByMaterialId.TryGetValue(material.Id, out var producers))
            {
                if (material.Level > 0)
                {
                    errors.Add(
                        $"Material '{material.Id}' (level {material.Level}) has no recipe producing it; " +
                        "it is unreachable in the production chain.");
                }

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
        GameConfig config, HashSet<string> sectorIds, HashSet<string> recipeIds, List<string> errors)
    {
        foreach (var factory in config.FactoryDefinitions)
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

                var recipe = config.Recipes.First(candidate => candidate.Id == recipeId);
                var outputMaterial = config.Materials.FirstOrDefault(material => material.Id == recipe.OutputMaterialId);
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

    private static void DetectCycles(GameConfig config, List<string> errors)
    {
        var recipeByOutputMaterialId = config.Recipes.ToDictionary(recipe => recipe.OutputMaterialId);
        var state = new Dictionary<string, int>();
        var path = new List<string>();

        // Iterate in JSON declaration order (not dictionary/hashset order) so error output is
        // deterministic (AGENTS §2 rule 6), even though this only affects error message ordering.
        foreach (var material in config.Materials)
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
