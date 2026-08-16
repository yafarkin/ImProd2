namespace Game.Bots.Llm;

/// <summary>Одна запись в <see cref="BotTurnHistory"/> — что бот решил на одном ходу и почему, его собственными словами.</summary>
public sealed record BotTurnHistoryEntry(int Turn, string Summary, string? Annotation);

/// <summary>
/// Собственная история решений LLM-бота поперёк ходов — то самое, что пользователь описал в первом
/// обсуждении идеи: короткие записи вида «build fab #0; set worker count fab #0 = 20» плюс
/// аннотация, которую модель сама себе оставляет, чтобы понимать прошлые решения на будущих ходах.
/// В отличие от <see cref="BotDecisionLog"/> (тот — попытки внутри одного хода, включая ошибки
/// парсинга/валидации) — здесь только итог хода, одна запись на ход. Хранит скользящее окно
/// последних <c>window</c> ходов — полная свёртка экономической истории сессии под контекст-окно
/// (риск №1 из обсуждения TODO #20) сюда не входит, это отдельная, всё ещё не решённая часть шага 4.
/// </summary>
public sealed class BotTurnHistory
{
    private readonly int _window;
    private readonly List<BotTurnHistoryEntry> _entries = new();

    public BotTurnHistory(int window = 10)
    {
        if (window < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "Must keep at least one turn.");
        }

        _window = window;
    }

    /// <summary>Записи в пределах окна, от самой старой к самой новой.</summary>
    public IReadOnlyList<BotTurnHistoryEntry> Entries => _entries;

    /// <summary>Добавляет запись об итоге хода; при переполнении окна вытесняет самую старую.</summary>
    public void Add(BotTurnHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries.Add(entry);
        if (_entries.Count > _window)
        {
            _entries.RemoveAt(0);
        }
    }

    /// <summary>Рендерит историю в текстовый блок для user-промпта.</summary>
    public string Render()
    {
        if (_entries.Count == 0)
        {
            return "YOUR PAST DECISIONS\n(none yet — this is your first turn)";
        }

        var lines = _entries.Select(entry => entry.Annotation is null
            ? $"- Turn {entry.Turn}: {entry.Summary}"
            : $"- Turn {entry.Turn}: {entry.Summary} — {entry.Annotation}");

        return $"YOUR PAST DECISIONS (most recent {_entries.Count})\n" + string.Join('\n', lines);
    }
}
