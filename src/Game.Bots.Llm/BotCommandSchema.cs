using System.Text.Json.Nodes;

namespace Game.Bots.Llm;

/// <summary>
/// JSON Schema формы <see cref="BotCommand"/> — на шагах 2-3 плана LLM-ботов передаётся LM
/// Studio/Ollama как <c>response_format</c>/<c>json_schema</c>, чтобы модель не отвечала свободным
/// текстом, а строго структурой (риск №2 из обсуждения TODO #20). На шаге 1 реального инференса нет
/// — схема используется только для структурной самопроверки (см. BotCommandSchemaTests), чтобы
/// список полей здесь не разошёлся с <see cref="BotCommand"/> незаметно.
/// </summary>
public static class BotCommandSchema
{
    /// <summary>Строит схему заново при каждом вызове — дешёвая операция, состояние не кешируется намеренно.</summary>
    public static JsonObject Build()
    {
        var kindEnum = new JsonArray();
        foreach (var name in Enum.GetNames<BotCommandKind>())
        {
            kindEnum.Add(name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("kind"),
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = kindEnum },
                ["factoryDefinitionId"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] = "Catalog id of a factory TYPE to build, e.g. 'iron-mine'. Only for kind=buildFactory.",
                },
                ["factoryId"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] =
                        "Id (ULID) of a factory this team ALREADY OWNS, taken from the state you were given — " +
                        "never a catalog type name. Not used with kind=buildFactory.",
                },
                ["recipeId"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] = "Catalog id of a recipe. Optional for kind=buildFactory, required for kind=selectRecipe.",
                },
                ["amount"] = new JsonObject
                {
                    ["type"] = new JsonArray("number", "null"),
                    ["description"] = "Money amount, for kind=takeLoan/repayLoan/setRndCommitment/setGenerationResearchCommitment.",
                },
                ["count"] = new JsonObject
                {
                    ["type"] = new JsonArray("integer", "null"),
                    ["description"] = "Number of workers, for kind=setWorkerCount.",
                },
                ["materialId"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] = "Catalog id of a material, for kind=sellToSystem/emergencyPurchase/postNeed.",
                },
                ["volume"] = new JsonObject
                {
                    ["type"] = new JsonArray("number", "null"),
                    ["description"] = "Volume of material, for kind=sellToSystem/emergencyPurchase.",
                },
                ["enabled"] = new JsonObject
                {
                    ["type"] = new JsonArray("boolean", "null"),
                    ["description"] = "Whether to request an overhaul, for kind=setOverhaulRequested.",
                },
                ["share"] = new JsonObject
                {
                    ["type"] = new JsonArray("number", "null"),
                    ["description"] = "Allocation weight for scarce input material, for kind=setFactoryAllocationShare.",
                },
                ["direction"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["enum"] = new JsonArray("surplus", "deficit", null),
                    ["description"] = "'surplus' (you have extra) or 'deficit' (you need it), for kind=postNeed.",
                },
                ["volumeOrder"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["enum"] = new JsonArray("small", "medium", "large", null),
                    ["description"] = "Rough size of the need, for kind=postNeed.",
                },
                ["comment"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] = "Optional free-text comment, for kind=postNeed.",
                },
                ["needId"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] = "Id of an existing need-board posting to withdraw, for kind=withdrawNeed.",
                },
                ["annotation"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] = "Free-text note you leave for yourself, to understand this decision on a future turn.",
                },
            },
        };
    }
}
