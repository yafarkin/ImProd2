using System.Net.Http.Json;
using System.Text.Json;

namespace Game.Bots.Llm.Jev;

public sealed record JevRequest(object State, IReadOnlyDictionary<string, string> Choices);
public sealed record JevAnswer(string Choice, string RawResponse);

public sealed class JevResponseException(string message, string rawResponse) : HttpRequestException(message)
{
    public string RawResponse { get; } = rawResponse;
}

public interface IJevClient
{
    Task<JevAnswer> ChooseAsync(JevRequest request, CancellationToken cancellationToken);
}

/// <summary>External decision service; never called by the deterministic engine.</summary>
public sealed class JevClient(HttpClient http, string model) : IJevClient
{
    public async Task<JevAnswer> ChooseAsync(JevRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("v1/systemone", new
        {
            model,
            state = request.State,
            questions = new
            {
                action = new
                {
                    type = "choice",
                    instructions = "Choose the best next action to develop a profitable complete production chain. " +
                        "Consider remaining time, input supply, recurring costs and market demand. Choose finish when no worthwhile action remains.",
                    criteria = request.Choices,
                },
            },
        }, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new JevResponseException($"JEV HTTP {(int)response.StatusCode}: {raw}", raw);
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("answers", out var answers)
                || answers.ValueKind != JsonValueKind.Object
                || !answers.TryGetProperty("action", out var action)
                || action.ValueKind != JsonValueKind.Object
                || !action.TryGetProperty("choice", out var choice)
                || choice.ValueKind != JsonValueKind.String
                || choice.GetString() is not { } selected
                || !request.Choices.ContainsKey(selected))
            {
                throw new JsonException("JEV response must select one of the supplied action IDs.");
            }
            return new JevAnswer(selected, raw);
        }
        catch (JsonException ex)
        {
            throw new JevResponseException($"Invalid JEV response: {ex.Message}", raw);
        }
    }
}
