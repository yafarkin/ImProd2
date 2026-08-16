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
                ["factoryDefinitionId"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["factoryId"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["recipeId"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["amount"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
                ["count"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                ["materialId"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["volume"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
                ["enabled"] = new JsonObject { ["type"] = new JsonArray("boolean", "null") },
                ["annotation"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            },
        };
    }
}
