using System.Text.Json;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>Commands retain the existing executor; short aliases are transport identifiers only.</summary>
public static class FileBotProtocol
{
    public static IReadOnlyList<string> Execute(string json, string requestId, GameSession session, Ulid teamId,
        IReadOnlyDictionary<string, Ulid> aliases, Random random, int maxActions)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("requestId").GetString() != requestId) throw new InvalidOperationException("Response requestId mismatch.");
        var actions = root.GetProperty("actions").EnumerateArray().ToArray();
        if (actions.Length > maxActions) throw new InvalidOperationException("Too many actions.");
        var parsed = actions.Select(a => (Command: a.Deserialize<BotCommand>(BotCommandSerialization.Options)
            ?? throw new JsonException("Null command."), Alias: a.TryGetProperty("factory", out var f) ? f.GetString() : null)).ToArray();
        var created = new Dictionary<string, Ulid>(StringComparer.Ordinal);
        var results = new List<string>();
        foreach (var (original, alias) in parsed)
        {
            var command = original;
            if (alias is not null)
            {
                Ulid id;
                var found = alias.StartsWith("new:", StringComparison.Ordinal)
                    ? created.TryGetValue(alias[4..], out id) : aliases.TryGetValue(alias, out id);
                if (!found) { results.Add($"error: unknown factory alias {alias}"); continue; }
                command = command with { FactoryId = id };
            }
            var result = new BotCommandExecutor().Execute(command, session, teamId, random);
            if (result is BotCommandExecutionResult.Success && command.Kind == BotCommandKind.BuildFactory)
                created[command.FactoryDefinitionId!] = session.State.Teams[teamId].Factories.Last().Id;
            results.Add(result switch
            {
                BotCommandExecutionResult.Success => $"ok:{command.Kind}",
                BotCommandExecutionResult.Nop => "nop",
                BotCommandExecutionResult.DomainError error => $"error:{error.Message}",
                _ => throw new InvalidOperationException(),
            });
        }
        return results;
    }
}
