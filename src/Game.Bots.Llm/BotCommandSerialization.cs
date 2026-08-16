using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Bots.Llm;

/// <summary>
/// Общие настройки сериализации <see cref="BotCommand"/> — camelCase-имена полей (привычнее для
/// JSON-вывода моделей, чем PascalCase) и <see cref="BotCommandKind"/> строкой, а не числом.
/// Используются и при разборе ответа модели (<see cref="LlmBotDecisionLoop"/>), и в тестах при
/// формировании сценариев для фейкового клиента — единая точка, чтобы то и другое не разъехалось.
/// </summary>
public static class BotCommandSerialization
{
    /// <summary>Опции для <see cref="JsonSerializer"/> при разборе/сборке <see cref="BotCommand"/>.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
