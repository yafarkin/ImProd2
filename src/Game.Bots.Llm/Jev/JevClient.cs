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
    Task<JevPlan> AssessAsync(JevPlanningRequest request, CancellationToken cancellationToken);
    Task<JevAnswer> ChooseAsync(JevRequest request, CancellationToken cancellationToken);
}

/// <summary>External decision service; never called by the deterministic engine.</summary>
public sealed class JevClient(HttpClient http, string model) : IJevClient
{
    public async Task<JevAnswer> ChooseAsync(JevRequest request, CancellationToken cancellationToken)
    {
        var raw = await SendAsync(request.State, new
        {
            action = new
            {
                type = "choice",
                instructions = "Choose the best next action to develop a profitable complete production chain. " +
                    "Use numeric analysis and any earlier assessment as advice, not a binding order. " +
                    "Consider remaining time, input supply, recurring costs and market demand. Choose finish when no worthwhile action remains.",
                criteria = request.Choices,
            },
        }, cancellationToken).ConfigureAwait(false);
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

    public async Task<JevPlan> AssessAsync(JevPlanningRequest request, CancellationToken cancellationToken)
    {
        var raw = await SendAsync(request.State, request.Questions, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(raw);
            var answers = document.RootElement.GetProperty("answers");
            string Choice(string key)
            {
                var selected = answers.GetProperty(key).GetProperty("choice").GetString();
                var allowed = (IReadOnlyDictionary<string, string>)request.Questions[key].Criteria!;
                if (selected is null || !allowed.ContainsKey(selected)) throw new JsonException($"Invalid assessment choice: {key}.");
                return selected;
            }
            var priority = Choice("priority");
            var target = Choice("target");
            var research = answers.GetProperty("research_now").GetProperty("noul").GetDouble();
            if (!double.IsFinite(research) || research < 0 || research > 1) throw new JsonException("Invalid research probability.");
            return new(priority, target, research, raw);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new JevResponseException($"Invalid JEV assessment: {ex.Message}", raw);
        }
    }

    private async Task<string> SendAsync(object state, object questions, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("v1/systemone", new { model, state, questions }, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new JevResponseException($"JEV HTTP {(int)response.StatusCode}: {raw}", raw);
        return raw;
    }

}
