using System.Text.Json;
using System.Text.Json.Nodes;

namespace Game.Bots.Llm.Tests;

/// <summary>
/// Защита от рассинхронизации между <see cref="BotCommandKind"/> и <see cref="BotCommandSchema"/> —
/// схема должна знать про каждое значение перечисления, иначе на шагах 2-3 (реальный
/// <c>response_format</c>/<c>json_schema</c>) модель не сможет вернуть валидную команду для
/// значения, добавленного в enum, но забытого в схеме.
/// </summary>
public sealed class BotCommandSchemaTests
{
    [Fact]
    public void Schema_ListsEveryCommandKindInCamelCase()
    {
        // camelCase, не сырое имя enum'а из Enum.GetNames — так же, как реально разбирает
        // BotCommandSerialization.Options (JsonStringEnumConverter(JsonNamingPolicy.CamelCase)).
        // Раньше схема звала PascalCase-именами, которые сам парсер не принимал — попутно найденный
        // и исправленный баг, см. doc-comment BotCommandSchema.BuildCommand.
        var schema = BotCommandSchema.BuildCommand();
        var kindEnum = ((JsonObject)schema["properties"]!["kind"]!)["enum"]!.AsArray();
        var listedNames = kindEnum.Select(node => node!.GetValue<string>()).ToHashSet();

        foreach (var name in Enum.GetNames<BotCommandKind>())
        {
            Assert.Contains(JsonNamingPolicy.CamelCase.ConvertName(name), listedNames);
        }
    }

    [Fact]
    public void Schema_RequiresKindAndReasonAndForbidsAdditionalProperties()
    {
        var schema = BotCommandSchema.BuildCommand();

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        Assert.Contains("kind", required);
        // Запрос пользователя 2026-08-19: «попросим модель объяснять каждое своё действие» —
        // reason обязателен для каждой команды, в отличие от необязательной annotation.
        Assert.Contains("reason", required);
        Assert.DoesNotContain("annotation", required);
    }

    [Fact]
    public void BuildBatch_WrapsCommandSchemaInActionsArray()
    {
        var batchSchema = BotCommandSchema.BuildBatch(maxActions: 4);

        Assert.Equal("object", batchSchema["type"]!.GetValue<string>());
        Assert.Contains("actions", batchSchema["required"]!.AsArray().Select(node => node!.GetValue<string>()));

        var actionsProperty = (JsonObject)batchSchema["properties"]!["actions"]!;
        Assert.Equal("array", actionsProperty["type"]!.GetValue<string>());
        Assert.Equal(4, actionsProperty["maxItems"]!.GetValue<int>());

        var itemSchema = (JsonObject)actionsProperty["items"]!;
        Assert.Equal("object", itemSchema["type"]!.GetValue<string>());
        Assert.Contains("kind", itemSchema["required"]!.AsArray().Select(node => node!.GetValue<string>()));
    }
}
