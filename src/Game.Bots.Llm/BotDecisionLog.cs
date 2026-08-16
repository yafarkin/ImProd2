using System.Text.Json;

namespace Game.Bots.Llm;

/// <summary>Одна запись в <see cref="BotDecisionLog"/> — одна попытка модели ответить за один ход.</summary>
public sealed record BotDecisionLogEntry(int Attempt, string RawResponse, string Outcome, DateTimeOffset Timestamp);

/// <summary>
/// Сырые ответы модели и итог каждой попытки — отдельно от доменного
/// <see cref="Game.Engine.EventLog{TState}"/> сессии (риск №4 из обсуждения TODO #20: не смешивать
/// с журналом решений сессии, но держать рядом для разбора «почему бот так решил»). На шаге 1 —
/// только накопление в памяти и сериализация в JSONL; настоящий файловый вывод появится вместе с
/// консольным раннером (шаг 2 плана).
/// </summary>
public sealed class BotDecisionLog
{
    private readonly List<BotDecisionLogEntry> _entries = new();
    private readonly Func<DateTimeOffset> _clock;

    public BotDecisionLog(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Все записи в порядке добавления.</summary>
    public IReadOnlyList<BotDecisionLogEntry> Entries => _entries;

    /// <summary>Добавляет запись об одной попытке.</summary>
    public void Record(int attempt, string rawResponse, string outcome)
    {
        _entries.Add(new BotDecisionLogEntry(attempt, rawResponse, outcome, _clock()));
    }

    /// <summary>Сериализует все записи построчно в JSONL — формат для файлового лога на шаге 2.</summary>
    public IEnumerable<string> ToJsonLines(JsonSerializerOptions? options = null)
    {
        foreach (var entry in _entries)
        {
            yield return JsonSerializer.Serialize(entry, options);
        }
    }
}
