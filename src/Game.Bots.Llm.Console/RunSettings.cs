using System.Globalization;

namespace Game.Bots.Llm.ConsoleApp;

/// <summary>
/// Настройки автономного прогона (запрос пользователя 2026-08-16: «собрал, запустил и ушёл спать») —
/// целиком из переменных окружения с разумными умолчаниями, чтобы .bat-файл на Windows мог
/// подставлять только то, что реально нужно поменять (адрес сервера, число ботов), не трогая код.
/// <see cref="LmStudioClient.DefaultBaseUrl"/> сам по себе уже читает <c>LM_STUDIO_BASE_URL</c> —
/// здесь только то, что специфично для консольного раннера.
/// </summary>
internal sealed record RunSettings(
    string Model,
    int BotCount,
    int Turns,
    int TimeoutMinutes,
    int MaxAttempts,
    int MaxConsecutiveFailures,
    int MaxActionsPerTurn,
    double Temperature,
    int MaxTokens,
    bool DisableThinking,
    string MetricsPath,
    string DecisionLogPath)
{
    public static RunSettings FromEnvironment()
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        return new RunSettings(
            Model: GetString("LLM_BOT_MODEL", "qwen/qwen3.8-27b"),
            BotCount: Math.Clamp(GetInt("LLM_BOT_COUNT", 3), 1, 8),
            Turns: GetInt("LLM_BOT_TURNS", 90),
            // Запрос пользователя: "тупо проснуться и узнать что отвалилось по таймауту через 20
            // минут" — щедрый таймаут ловит настоящее зависание, не режет честное долгое размышление
            // (замеры 2026-08-16: реальный ход занимал 2-5 минут на этом сервере).
            TimeoutMinutes: GetInt("LLM_BOT_TIMEOUT_MINUTES", 20),
            // "retry повыше" — с потолком выше библиотечного умолчания (3), чтобы разовые сетевые
            // сбои за ночь не срывали отдельные ходы. Запрос пользователя 2026-08-16 (позже): один
            // вызов LLM на весь ход, а не на действие — это попыток на ВЕСЬ ход (битый JSON/сетевая
            // ошибка), не на отдельное действие, как было раньше (см. doc-comment LlmBotDecisionLoop).
            MaxAttempts: GetInt("LLM_BOT_MAX_ATTEMPTS", 6),
            // Выше умолчания LlmBotSessionRunner (3) — при нескольких ботах не хотим останавливать
            // весь прогон из-за временных проблем у одного бота; это по-прежнему страховка на случай
            // настоящей поломки (например, сервер лёг), не бесконечное ожидание.
            MaxConsecutiveFailures: GetInt("LLM_BOT_MAX_CONSECUTIVE_FAILURES", 8),
            // Потолок длины массива действий в ОДНОМ ответе модели (запрос пользователя 2026-08-16:
            // один вызов LLM на весь ход, сразу массив команд, — см. doc-comment LlmBotDecisionLoop)
            // — избыток сверх него молча отбрасывается. История значения: было 8 (когда это было
            // потолком числа ВЫЗОВОВ LLM за ход, ещё многовызовная версия) → снижено до 5 при переходе
            // на батч (живые прогоны gemma-2-9b-it/qwen3.8-27b показали модели, надёжно доходящие до
            // потолка, штампуя одно и то же) → поднято обратно до 8 (2026-08-17, разбор первого
            // полного 90-ходового батч-прогона — оба анти-залипательных guard'а в LlmBotDecisionLoop
            // не сработали НИ РАЗУ за 270 вызовов, а 63% ходов упирались именно в потолок 5, обрезая
            // легитимную продажу выпуска у ботов с 10+ фабриками; см.
            // docs/bot-runs/2026-08-16-stage1-qwen3.8-27b/ANALYSIS.md). Guard'ы, не число, — реальная
            // защита от залипания; если новый прогон покажет, что guard'ы снова начали срабатывать
            // часто на этом потолке, разбираться нужно ими, не снижать потолок обратно вслепую.
            MaxActionsPerTurn: GetInt("LLM_BOT_MAX_ACTIONS_PER_TURN", 8),
            Temperature: GetDouble("LLM_BOT_TEMPERATURE", 0.4),
            MaxTokens: GetInt("LLM_BOT_MAX_TOKENS", 3000),
            // Запрос пользователя 2026-08-16: reasoning жрёт токены и сильно замедляет каждый ход —
            // по умолчанию выключен (см. doc-comment LmStudioClient, chat_template_kwargs); поставьте
            // LLM_BOT_DISABLE_THINKING=0, если для конкретной модели reasoning всё же нужен.
            DisableThinking: GetBool("LLM_BOT_DISABLE_THINKING", true),
            MetricsPath: GetString("LLM_BOT_METRICS_PATH", Path.Combine(AppContext.BaseDirectory, $"metrics-{timestamp}.csv")),
            DecisionLogPath: GetString("LLM_BOT_DECISIONS_PATH", Path.Combine(AppContext.BaseDirectory, $"decisions-{timestamp}.jsonl")));
    }

    private static string GetString(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static int GetInt(string name, int fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static double GetDouble(string name, double fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool GetBool(string name, bool fallback) =>
        Environment.GetEnvironmentVariable(name) switch
        {
            "0" => false,
            "1" => true,
            { Length: > 0 } value => bool.TryParse(value, out var parsed) ? parsed : fallback,
            _ => fallback,
        };
}
