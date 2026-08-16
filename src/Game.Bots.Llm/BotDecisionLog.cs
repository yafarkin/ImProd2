using System.Text.Json;

namespace Game.Bots.Llm;

/// <summary>
/// Одна запись в <see cref="BotDecisionLog"/> — одна попытка модели ответить за один ход одного
/// бота. <see cref="UserPrompt"/> — именно тот текст, что был отправлен на этой попытке (при
/// ретрае в него уже дописан текст предыдущей ошибки), не заглушка и не хэш — запрос пользователя
/// 2026-08-16: «лог запросов/ответов, включая конечно последний», чтобы упавший прогон можно было
/// понять по одному файлу, а не переспрашивать «а что вообще отправляли».
/// </summary>
public sealed record BotDecisionLogEntry(
    string BotLabel, int Turn, int Attempt, string UserPrompt, string RawResponse, string Outcome, DateTimeOffset Timestamp);

/// <summary>
/// Сырые запросы и ответы модели, попытка за попыткой, — отдельно от доменного
/// <see cref="Game.Engine.EventLog{TState}"/> сессии (не смешивать с журналом решений сессии, но
/// держать рядом для разбора «почему бот так решил»), и отдельно от <see cref="BotMetricsLog"/>
/// (та — числа для перцентилей, эта — содержимое для чтения).
/// <para>
/// <see cref="CreateFile"/> пишет каждую попытку на диск сразу же, не только в памяти (запрос
/// пользователя 2026-08-16: «если упадёт — всё, что было наработано, должно остаться на диске»,
/// включая саму последнюю, возможно неудачную попытку перед падением — конструктор без параметров
/// копит только в памяти и годится для тестов, но не для многочасового автономного прогона).
/// </para>
/// </summary>
public sealed class BotDecisionLog : IDisposable
{
    private readonly List<BotDecisionLogEntry> _entries = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly TextWriter? _writer;
    private readonly bool _ownsWriter;

    public BotDecisionLog(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private BotDecisionLog(TextWriter writer)
    {
        _writer = writer;
        _ownsWriter = true;
        _clock = () => DateTimeOffset.UtcNow;
    }

    /// <summary>Открывает (дозаписывает) реальный JSONL-файл с автосбросом на диск после каждой попытки — см. doc-comment класса.</summary>
    public static BotDecisionLog CreateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var stream = new StreamWriter(path, append: true) { AutoFlush = true };
        return new BotDecisionLog(stream);
    }

    /// <summary>Все записи в порядке добавления (в памяти — доступны и при файловом режиме, для тестов и промежуточных отчётов).</summary>
    public IReadOnlyList<BotDecisionLogEntry> Entries => _entries;

    /// <summary>Добавляет запись об одной попытке одного бота на одном ходу.</summary>
    public void Record(string botLabel, int turn, int attempt, string userPrompt, string rawResponse, string outcome)
    {
        var entry = new BotDecisionLogEntry(botLabel, turn, attempt, userPrompt, rawResponse, outcome, _clock());
        _entries.Add(entry);
        _writer?.WriteLine(JsonSerializer.Serialize(entry));
    }

    /// <summary>Сериализует накопленные в памяти записи построчно в JSONL — для режима без файла (тесты, разовые прогоны).</summary>
    public IEnumerable<string> ToJsonLines(JsonSerializerOptions? options = null)
    {
        foreach (var entry in _entries)
        {
            yield return JsonSerializer.Serialize(entry, options);
        }
    }

    public void Dispose()
    {
        if (_ownsWriter)
        {
            _writer?.Dispose();
        }
    }
}
