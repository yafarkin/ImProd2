using System.Text.Json.Serialization;

namespace Game.Bots.Llm.Jev;

public sealed record JevQuestion(string Type, string Instructions, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Criteria = null);
public sealed record JevPlanningRequest(object State, IReadOnlyDictionary<string, JevQuestion> Questions);
public sealed record JevPlan(string Priority, string TargetRecipe, double ResearchNowProbability, string RawResponse);
public sealed record JevPlanningRecord(int Turn, JevPlanningRequest Request, JevPlan? Answer,
    string? RawResponse, string Outcome, double ElapsedMilliseconds);

public static class JevPlanning
{
    public static JevPlanningRequest Create(object state, JevAnalysis analysis)
    {
        var targets = new Dictionary<string, string>(StringComparer.Ordinal) { ["none"] = "No new production recipe this turn; manage existing assets." };
        foreach (var development in analysis.Development)
            targets.Add(development.Recipe, $"Develop {development.Output} with {development.Factory}; see numeric development estimates.");
        return new(state, new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["priority"] = new("choice", "What deserves management attention this turn, given cash trend, wear and development opportunities?",
                new Dictionary<string, string>
                {
                    ["develop"] = "Develop the production chain, including research and preparing inputs.",
                    ["repair"] = "Prevent breakdown and restore productive assets.",
                    ["stabilize"] = "Address losses and input shortages before expansion.",
                    ["operate"] = "Operate and sell surplus; defer new investment.",
                }),
            ["target"] = new("choice", "Which candidate production recipe is the most useful development target? A locked recipe can require research first. Choose none if development is not justified.", targets),
            ["research_now"] = new("noul", "Should recurring generation research be started or continued now, considering remaining time, costs, supply and development prospects?"),
        });
    }
}
