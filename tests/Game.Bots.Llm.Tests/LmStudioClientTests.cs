using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Game.Bots.Llm.Tests;

/// <summary>
/// <see cref="LmStudioClient"/> против поддельного HTTP-обработчика — без сети, без зависимости от
/// реально запущенного LM Studio (то, что нужно CI). Ответы — в реальном SSE-формате (запрос
/// пользователя 2026-08-16: streaming вместо ожидания всего ответа целиком), форма которого взята с
/// живых прогонов против LM Studio, см. doc-comment <see cref="LmStudioClient"/>.
/// </summary>
public sealed class LmStudioClientTests
{
    /// <summary>Собирает тело SSE-потока из кусков content/reasoning_content, как их шлёт LM Studio, плюс завершающий "data: [DONE]".</summary>
    private static string BuildSseBody(params (string? Content, string? Reasoning)[] deltas)
    {
        var body = new StringBuilder();
        foreach (var (content, reasoning) in deltas)
        {
            var delta = new JsonObject();
            if (content is not null)
            {
                delta["content"] = content;
            }
            if (reasoning is not null)
            {
                delta["reasoning_content"] = reasoning;
            }

            var chunk = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = delta }) };
            body.Append("data: ").Append(chunk.ToJsonString()).Append("\n\n");
        }

        body.Append("data: [DONE]\n\n");
        return body.ToString();
    }

    [Fact]
    public async Task CompleteAsync_AssemblesContentAcrossStreamedChunks()
    {
        var handler = new StubHttpMessageHandler(BuildSseBody(
            ("""{"kind": "buildFactory", """, null),
            ("""  "factoryDefinitionId": "iron-mine"}""", null)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b");

        var content = await client.CompleteAsync("system", "user");

        Assert.Equal("""{"kind": "buildFactory",   "factoryDefinitionId": "iron-mine"}""", content);
    }

    [Fact]
    public async Task CompleteAsync_ParsedContentDeserializesToExpectedCommand()
    {
        var handler = new StubHttpMessageHandler(BuildSseBody(
            ("""{"kind": "buildFactory", "factoryDefinitionId": "iron-mine", "recipeId": null}""", null)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b");

        var content = await client.CompleteAsync("system", "user");
        var command = System.Text.Json.JsonSerializer.Deserialize<BotCommand>(content, BotCommandSerialization.Options);

        Assert.Equal(BotCommandKind.BuildFactory, command!.Kind);
        Assert.Equal("iron-mine", command.FactoryDefinitionId);
    }

    [Fact]
    public async Task CompleteAsync_InvokesOnTokenOncePerChunk()
    {
        var handler = new StubHttpMessageHandler(BuildSseBody(("a", null), ("b", null), ("c", null)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var counts = new List<int>();
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b", onToken: counts.Add);

        var content = await client.CompleteAsync("system", "user");

        Assert.Equal("abc", content);
        Assert.Equal([1, 2, 3], counts);
    }

    [Fact]
    public async Task CompleteAsync_SendsModelMessagesStreamTrueAndJsonSchemaResponseFormat()
    {
        var handler = new StubHttpMessageHandler(BuildSseBody(("ok", null)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b", temperature: 0.3, maxTokens: 123);

        await client.CompleteAsync("be careful", "build a factory");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(new Uri("http://localhost:1234/v1/chat/completions"), handler.LastRequest.RequestUri);

        var body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        Assert.Equal("google/gemma-4-12b", body["model"]!.GetValue<string>());
        Assert.Equal(0.3, body["temperature"]!.GetValue<double>());
        Assert.Equal(123, body["max_tokens"]!.GetValue<int>());
        Assert.True(body["stream"]!.GetValue<bool>());

        var messages = body["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("be careful", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Equal("build a factory", messages[1]!["content"]!.GetValue<string>());

        var responseFormat = body["response_format"]!.AsObject();
        Assert.Equal("json_schema", responseFormat["type"]!.GetValue<string>());
        var jsonSchema = responseFormat["json_schema"]!.AsObject();
        Assert.False(jsonSchema["strict"]!.GetValue<bool>());
        Assert.Equal("object", jsonSchema["schema"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task CompleteAsync_NonSuccessStatusCode_ThrowsWithBodyInMessage()
    {
        // Ошибки не стримятся — сервер возвращает обычное JSON-тело до начала генерации.
        var handler = new StubHttpMessageHandler("""{"error":"model not loaded"}""", HttpStatusCode.ServiceUnavailable);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync("s", "u"));

        Assert.Contains("503", ex.Message);
        Assert.Contains("model not loaded", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_EmptyStream_Throws()
    {
        var handler = new StubHttpMessageHandler("data: [DONE]\n\n");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync("s", "u"));
    }

    [Fact]
    public async Task CompleteAsync_OnlyReasoningContentChunks_FallsBackToReasoningContent()
    {
        // Живой прогон 2026-08-16 с reasoning-моделью (qwen3.8-27b-mlx): весь ответ приходит через
        // reasoning_content-дельты, "content" не появляется вовсе.
        var handler = new StubHttpMessageHandler(BuildSseBody(
            (null, """{"kind": "buildFactory", """),
            (null, "\"factoryDefinitionId\": \"iron-mine\"}")));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "qwen3.8-27b-mlx");

        var content = await client.CompleteAsync("s", "u");

        Assert.Equal("""{"kind": "buildFactory", "factoryDefinitionId": "iron-mine"}""", content);
    }
}
