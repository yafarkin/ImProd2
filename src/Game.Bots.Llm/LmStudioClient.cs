using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Game.Bots.Llm;

/// <summary>
/// Реализация <see cref="ILlmClient"/> для LM Studio (шаг 2 плана LLM-ботов, docs/TODO.md #20) —
/// OpenAI-совместимый <c>/v1/chat/completions</c>, поэтому та же реализация без изменений подходит
/// и для Ollama (шаг 3), если у переданного <see cref="HttpClient"/> выставить его base address.
/// Ответ запрашивается как <c>response_format: json_schema</c> по <see cref="BotCommandSchema"/>
/// (риск №2 из обсуждения TODO #20) — но <c>strict</c> сознательно <see langword="false"/> и
/// в required только <c>kind</c>: живая проверка на LM Studio (gemma-4-12b, 2026-08-16) показала,
/// что при <c>strict: true</c>, требующем перечислить в required вообще все поля схемы, модель не
/// умеет оставить нерелевантное поле пустым — вместо null подставляет правдоподобный мусор
/// (придуманный <c>factoryId</c>, гигантские числа в <c>amount</c>/<c>count</c>), что бьёт по
/// парсингу (переполнение <see langword="int"/>) чаще, чем просто отсутствие поля.
/// </summary>
public sealed class LmStudioClient : ILlmClient
{
    /// <summary>Адрес LM Studio по умолчанию при локальном запуске.</summary>
    public const string DefaultBaseUrl = "http://localhost:1234/v1/";

    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly double _temperature;
    private readonly int _maxTokens;

    /// <summary>
    /// <paramref name="httpClient"/> должен иметь выставленный <see cref="HttpClient.BaseAddress"/>
    /// (например, <see cref="DefaultBaseUrl"/>) — сам клиент не создаёт и не владеет
    /// <see cref="HttpClient"/>, чтобы вызывающая сторона управляла его временем жизни
    /// (<c>IHttpClientFactory</c> в реальном раннере, поддельный обработчик в тестах).
    /// </summary>
    public LmStudioClient(HttpClient httpClient, string model, double temperature = 0.2, int maxTokens = 500)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model id must not be empty.", nameof(model));
        }

        _httpClient = httpClient;
        _model = model;
        _temperature = temperature;
        _maxTokens = maxTokens;
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(userPrompt);

        var requestBody = BuildRequestBody(systemPrompt, userPrompt);
        using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"LM Studio request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {responseText}");
        }

        return ExtractMessageContent(responseText);
    }

    private JsonObject BuildRequestBody(string systemPrompt, string userPrompt) => new()
    {
        ["model"] = _model,
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = userPrompt }),
        ["temperature"] = _temperature,
        ["max_tokens"] = _maxTokens,
        ["response_format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "bot_command",
                ["strict"] = false,
                ["schema"] = BotCommandSchema.Build(),
            },
        },
    };

    private static string ExtractMessageContent(string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"LM Studio response had no choices: {responseText}");
        }

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"LM Studio response message had no text content: {responseText}");
        }

        return contentElement.GetString()!;
    }
}
