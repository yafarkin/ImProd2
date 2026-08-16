using System.Globalization;

namespace Game.Bots.Llm;

/// <summary>
/// Простейший CSV-файл метрик по ходам LLM-бота (запрос пользователя 2026-08-16): бот, ход, время
/// ответа, размер запроса в байтах, отвеченная команда — сырьё для перцентилей позже (p50/p85
/// времени ответа — «как долго бот думает»; p50/p85/макс и рост размера запроса между ходами — как
/// быстро растёт промпт с историей). Раздельно от <see cref="BotDecisionLog"/>: тот хранит сырые
/// промпты/ответы по каждой попытке ретрая, этот — по одной строке на реальный ход, для статистики,
/// а не для разбора «почему бот так решил».
/// <para>
/// «Размер запроса» — байты UTF-8 системного+пользовательского промпта, переданных в
/// <see cref="ILlmClient.CompleteAsync"/> на первой попытке хода (без текста ошибок ретраев, если
/// они были, — растёт именно снапшот состояния и история, это они интересуют пользователя, не
/// вариации внутри одного хода). Не точный размер тела HTTP-запроса (JSON-обвязка, схема — примерно
/// постоянный довесок, не искажает тренд по ходам), см. doc-comment <see cref="LlmBot"/>.
/// </para>
/// </summary>
public sealed class BotMetricsLog : IDisposable
{
    private const string Header = "bot,turn,response_time_ms,request_size_bytes,command";

    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;

    /// <summary>
    /// Пишет через уже открытый <paramref name="writer"/> (например, <see cref="StringWriter"/> в
    /// тестах) и сразу пишет заголовок — вызывающая сторона решает, что делать с потоком дальше, но
    /// не владеет им (<see cref="Dispose"/> его не закрывает). Для реального файла используйте
    /// <see cref="Create"/>.
    /// </summary>
    public BotMetricsLog(TextWriter writer)
        : this(writer, ownsWriter: false)
    {
        _writer.WriteLine(Header);
    }

    private BotMetricsLog(TextWriter writer, bool ownsWriter)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _ownsWriter = ownsWriter;
    }

    /// <summary>
    /// Открывает (дозаписывает) реальный CSV-файл по <paramref name="path"/> — заголовок пишется
    /// только если файл ещё не существовал или был пуст, чтобы можно было безопасно продолжать один
    /// и тот же файл метрик между перезапусками одного прогона. Автосброс на диск после каждой
    /// строки — прогон может идти долго (десятки ходов × секунды на ответ модели), файл должен быть
    /// читаем «на лету», не только после штатного завершения процесса.
    /// </summary>
    public static BotMetricsLog Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var needsHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        var stream = new StreamWriter(path, append: true) { AutoFlush = true };
        if (needsHeader)
        {
            stream.WriteLine(Header);
        }

        return new BotMetricsLog(stream, ownsWriter: true);
    }

    /// <summary>Добавляет одну строку — один реальный ход одного бота.</summary>
    public void Record(string botLabel, int turn, TimeSpan responseTime, int requestSizeBytes, string command)
    {
        ArgumentNullException.ThrowIfNull(botLabel);
        ArgumentNullException.ThrowIfNull(command);
        if (requestSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestSizeBytes), requestSizeBytes, "Request size must not be negative.");
        }

        var fields = new[]
        {
            EscapeCsvField(botLabel),
            turn.ToString(CultureInfo.InvariantCulture),
            responseTime.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
            requestSizeBytes.ToString(CultureInfo.InvariantCulture),
            EscapeCsvField(command),
        };

        _writer.WriteLine(string.Join(',', fields));
    }

    public void Dispose()
    {
        if (_ownsWriter)
        {
            _writer.Dispose();
        }
    }

    private static string EscapeCsvField(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
