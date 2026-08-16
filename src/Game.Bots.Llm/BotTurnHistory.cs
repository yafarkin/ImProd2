namespace Game.Bots.Llm;

/// <summary>Одно действие, которое бот предпринял (или не предпринял) на одном ходу.</summary>
public sealed record BotTurnActionRecord(string Summary, string? Annotation);

/// <summary>
/// Одна запись в <see cref="BotTurnHistory"/> — все действия, которые бот предпринял на одном ходу,
/// по порядку (ход может состоять из нескольких действий подряд, запрос пользователя 2026-08-16 —
/// «важно уметь несколько команд за один ход»; всегда хотя бы одно — либо реальные действия, либо
/// одинокий <c>nop</c>).
/// </summary>
public sealed record BotTurnHistoryEntry(int Turn, IReadOnlyList<BotTurnActionRecord> Actions);

/// <summary>
/// Собственная история решений LLM-бота поперёк ходов — то самое, что пользователь описал в первом
/// обсуждении идеи: короткие записи вида «build fab #0; set worker count fab #0 = 20» плюс
/// аннотация, которую модель сама себе оставляет, чтобы понимать прошлые решения на будущих ходах.
/// В отличие от <see cref="BotDecisionLog"/> (тот — попытки внутри одного действия, включая ошибки
/// парсинга/валидации) — здесь только итог хода целиком, одна запись на ход (но с несколькими
/// действиями внутри). Хранит скользящее окно последних <c>window</c> ходов — полная свёртка
/// экономической истории сессии под контекст-окно (риск №1 из обсуждения TODO #20) сюда не входит,
/// это отдельная, всё ещё не решённая часть шага 4.
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

    /// <summary>Рендерит историю в текстовый блок для user-промпта — действия одного хода через «; ».</summary>
    public string Render()
    {
        if (_entries.Count == 0)
        {
            return "YOUR PAST DECISIONS\n(none yet — this is your first turn)";
        }

        var lines = _entries.Select(entry =>
        {
            var actions = string.Join("; ", entry.Actions.Select(a => a.Annotation is null
                ? a.Summary
                : $"{a.Summary} — {a.Annotation}"));
            return $"- Turn {entry.Turn}: {actions}";
        });

        return $"YOUR PAST DECISIONS (most recent {_entries.Count})\n" + string.Join('\n', lines);
    }
}
