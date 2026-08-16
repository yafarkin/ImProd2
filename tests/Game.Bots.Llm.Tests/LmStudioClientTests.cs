using System.Net;
using System.Text.Json.Nodes;

namespace Game.Bots.Llm.Tests;

/// <summary>
/// <see cref="LmStudioClient"/> против поддельного HTTP-обработчика — без сети, без зависимости от
/// реально запущенного LM Studio (то, что нужно CI). Форма запроса/ответа взята с живого прогона
/// против LM Studio (gemma-4-12b) 2026-08-16, см. doc-comment <see cref="LmStudioClient"/>.
/// </summary>
public sealed class LmStudioClientTests
{
    private const string SampleSuccessResponse = """
        {
          "id": "chatcmpl-1",
          "object": "chat.completion",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": "{\"kind\": \"buildFactory\", \"factoryDefinitionId\": \"iron-mine\", \"recipeId\": null}"
              },
              "finish_reason": "stop"
            }
          ]
        }
        """;

    [Fact]
    public async Task CompleteAsync_ExtractsMessageContentFromResponse()
    {
        var handler = new StubHttpMessageHandler(SampleSuccessResponse);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b");

        var content = await client.CompleteAsync("system", "user");

        Assert.Equal("""{"kind": "buildFactory", "factoryDefinitionId": "iron-mine", "recipeId": null}""", content);
    }

    [Fact]
    public async Task CompleteAsync_ParsedContentDeserializesToExpectedCommand()
    {
        var handler = new StubHttpMessageHandler(SampleSuccessResponse);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b");

        var content = await client.CompleteAsync("system", "user");
        var command = System.Text.Json.JsonSerializer.Deserialize<BotCommand>(content, BotCommandSerialization.Options);

        Assert.Equal(BotCommandKind.BuildFactory, command!.Kind);
        Assert.Equal("iron-mine", command.FactoryDefinitionId);
    }

    [Fact]
    public async Task CompleteAsync_SendsModelMessagesAndJsonSchemaResponseFormat()
    {
        var handler = new StubHttpMessageHandler(SampleSuccessResponse);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b", temperature: 0.3, maxTokens: 123);

        await client.CompleteAsync("be careful", "build a factory");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(new Uri("http://localhost:1234/v1/chat/completions"), handler.LastRequest.RequestUri);

        var body = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
        Assert.Equal("google/gemma-4-12b", body["model"]!.GetValue<string>());
        Assert.Equal(0.3, body["temperature"]!.GetValue<double>());
        Assert.Equal(123, body["max_tokens"]!.GetValue<int>());

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
        var handler = new StubHttpMessageHandler("""{"error":"model not loaded"}""", HttpStatusCode.ServiceUnavailable);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync("s", "u"));

        Assert.Contains("503", ex.Message);
        Assert.Contains("model not loaded", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_NoChoices_Throws()
    {
        var handler = new StubHttpMessageHandler("""{"choices":[]}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl) };
        var client = new LmStudioClient(httpClient, "google/gemma-4-12b");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync("s", "u"));
    }
}
