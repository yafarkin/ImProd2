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
    public void Schema_ListsEveryCommandKind()
    {
        var schema = BotCommandSchema.Build();
        var kindEnum = ((JsonObject)schema["properties"]!["kind"]!)["enum"]!.AsArray();
        var listedNames = kindEnum.Select(node => node!.GetValue<string>()).ToHashSet();

        foreach (var name in Enum.GetNames<BotCommandKind>())
        {
            Assert.Contains(name, listedNames);
        }
    }

    [Fact]
    public void Schema_RequiresKindAndForbidsAdditionalProperties()
    {
        var schema = BotCommandSchema.Build();

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Contains("kind", schema["required"]!.AsArray().Select(node => node!.GetValue<string>()));
    }
}
