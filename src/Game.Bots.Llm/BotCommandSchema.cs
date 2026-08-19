using System.Text.Json;
using System.Text.Json.Nodes;

namespace Game.Bots.Llm;

/// <summary>
/// JSON Schema формы <see cref="BotCommand"/> и обёртки <see cref="BotCommandBatch"/> вокруг неё —
/// передаётся LM Studio/Ollama как <c>response_format</c>/<c>json_schema</c>, чтобы модель не
/// отвечала свободным текстом, а строго структурой (риск №2 из обсуждения TODO #20). Схема
/// используется и для структурной самопроверки (см. BotCommandSchemaTests), чтобы список полей здесь
/// не разошёлся с <see cref="BotCommand"/> незаметно.
/// </summary>
public static class BotCommandSchema
{
    /// <summary>
    /// Схема ответа на весь ход разом (запрос пользователя 2026-08-16: один вызов LLM за ход — см.
    /// doc-comment <see cref="BotCommandBatch"/>) — объект с единственным полем <c>actions</c>,
    /// массивом объектов формы <see cref="BuildCommand"/>. <paramref name="maxActions"/> — мягкий
    /// потолок длины массива на уровне схемы (генерация не может уйти в бесконечность); настоящее
    /// принудительное ограничение — по-прежнему в коде (<see cref="LlmBotDecisionLoop"/>), схема не
    /// гарантия для бэкендов, не проверяющих массивы при <c>strict: false</c>.
    /// </summary>
    public static JsonObject BuildBatch(int maxActions = 10) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("actions"),
        ["properties"] = new JsonObject
        {
            ["actions"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = maxActions,
                ["items"] = BuildCommand(),
            },
        },
    };

    /// <summary>Схема одной команды — строится заново при каждом вызове, дешёвая операция, состояние не кешируется намеренно.</summary>
    public static JsonObject BuildCommand()
    {
        // Найденный попутно баг (не связан с батч-переделкой 2026-08-16): Enum.GetNames возвращает
        // PascalCase ("BuildFactory"), а BotCommandSerialization.Options разбирает kind camelCase'ом
        // ("buildFactory", JsonStringEnumConverter(JsonNamingPolicy.CamelCase)) — схема годами звала
        // модель значениями, которые её же парсер не ждал. Живьём это не било, потому что модель
        // следовала явным camelCase-примерам в SystemPromptBuilder (CommandDescriptions), а не enum'у
        // схемы, но схема должна документировать то, что реально принимается.
        var kindEnum = new JsonArray();
        foreach (var name in Enum.GetNames<BotCommandKind>())
        {
            kindEnum.Add(JsonNamingPolicy.CamelCase.ConvertName(name));
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
                    ["description"] = "Catalog id of a material, for kind=sellToSystem/emergencyPurchase/postNeed/postSellOffer/postBuyOffer.",
                },
                ["volume"] = new JsonObject
                {
                    ["type"] = new JsonArray("number", "null"),
                    ["description"] =
                        "Volume of material, for kind=sellToSystem/emergencyPurchase/postSellOffer/postBuyOffer (per delivery) " +
                        "and kind=fulfillTradeOffer (how much of the offer to take).",
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
                ["recurring"] = new JsonObject
                {
                    ["type"] = new JsonArray("boolean", "null"),
                    ["description"] =
                        "true = delivered every turn until fulfilled or withdrawn, false/omitted = a one-off delivery. " +
                        "For kind=postSellOffer/postBuyOffer.",
                },
                ["minPrice"] = new JsonObject
                {
                    ["type"] = new JsonArray("number", "null"),
                    ["description"] = "Lowest unit price you'd accept, for kind=postSellOffer/postBuyOffer.",
                },
                ["maxPrice"] = new JsonObject
                {
                    ["type"] = new JsonArray("number", "null"),
                    ["description"] = "Highest unit price you'd accept, for kind=postSellOffer/postBuyOffer.",
                },
                ["tradeOfferId"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] =
                        "Id of an existing public trade offer, copied verbatim from the state below, for " +
                        "kind=withdrawTradeOffer (your own) or kind=fulfillTradeOffer (someone else's).",
                },
                ["unitPrice"] = new JsonObject
                {
                    ["type"] = new JsonArray("number", "null"),
                    ["description"] = "The exact price you offer, within the offer's price range, for kind=fulfillTradeOffer.",
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
