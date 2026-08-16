// Автономный прогон LLM-ботов на стадии 1 (запрос пользователя 2026-08-16): собрать, запустить под
// Windows и уйти спать — устойчивый к отдельным сбоям (щедрые таймауты и ретраи, остановка сессии
// целиком только при явно застрявшем боте), с построчным статусом на экран в реальном времени
// ("бот 2, ход 14, запрос к LLM...", "бот 2, ход 14, TakeLoan за 03:12"), плюс CSV-метрики и сырой
// JSONL-лог решений на диск — не только на экран, переживает закрытие консоли.

using System.Text;
using Game.Bots.Llm;
using Game.Bots.Llm.ConsoleApp;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

// Явно, не полагаясь на кодовую страницу консоли по умолчанию (на Windows без этого кириллица в
// echo/Console.Write может исказиться, даже если .bat уже сделал chcp 65001).
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // Вывод перенаправлен не в реальную консоль (файл, конвейер) — менять кодовую страницу некому,
    // ничего страшного, сама запись в файл всё равно останется в UTF-8 (StreamWriter ниже).
}

var settings = RunSettings.FromEnvironment();
var logPath = Path.Combine(AppContext.BaseDirectory, $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
using var logFile = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };

// Живой счётчик кусков ответа (см. ниже, OnToken) пишет поверх текущей строки через \r, не давая
// начинать новую на каждый кусок, — эта отметка нужна, чтобы Log() знал, что перед его собственной
// строкой надо сперва закрыть недописанную строку прогресса переводом строки, иначе они склеятся.
var midStreamProgress = false;

void Log(string line)
{
    if (midStreamProgress)
    {
        Console.WriteLine();
        midStreamProgress = false;
    }

    var stamped = $"[{DateTimeOffset.Now:HH:mm:ss}] {line}";
    Console.WriteLine(stamped);
    logFile.WriteLine(stamped);
}

Log("=== LLM-боты, стадия 1 (один сектор), автономный прогон ===");
Log($"LM Studio: {LmStudioClient.DefaultBaseUrl}");
Log($"Модель: {settings.Model}, температура: {settings.Temperature}, max_tokens: {settings.MaxTokens}");
Log($"Ботов: {settings.BotCount}, ходов до конца сессии: {settings.Turns}");
Log($"HTTP-таймаут запроса: {settings.TimeoutMinutes} мин, попыток на ход: {settings.MaxAttempts}, " +
    $"остановка после {settings.MaxConsecutiveFailures} провалов подряд у одного бота");
Log($"Лог на диске: {logPath}");
Log($"Метрики: {settings.MetricsPath}");
Log($"Сырой лог решений: {settings.DecisionLogPath}");
Log("");

try
{
    var productionModelPath = Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "metallurgy.json");
    var sessionPath = Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "pilot.json");
    var config = GameConfigLoader.LoadFromFiles(productionModelPath, sessionPath);

    // Три разные персоны на выбор — не число в формуле, а текст, который модель сама интерпретирует
    // (осознанный выбор дизайна LLM-ботов, TODO.md #20). При меньшем числе ботов берутся первые N.
    string[] personas =
    [
        "You are cautious and risk-averse: you avoid debt when possible, keep a large cash buffer, " +
        "and only build new production capacity when you're confident it will pay off.",
        "You are a balanced, pragmatic team manager — neither especially fearful nor especially " +
        "greedy. You take measured risks and don't leave obvious opportunities on the table.",
        "You are ambitious and growth-focused: you take on debt readily to expand production capacity " +
        "fast, betting that scale pays off before it becomes a problem.",
    ];

    var teamSpecs = new List<TeamSpec>();
    var teamIds = new List<Ulid>();
    for (var i = 0; i < settings.BotCount; i++)
    {
        var id = Ulid.NewUlid();
        teamIds.Add(id);
        teamSpecs.Add(new TeamSpec { Id = id, Name = $"Бот {i + 1}", SectorId = "A" });
    }

    var session = GameSession.StartWithEndTurn(config, "full", settings.Turns, teamSpecs);
    // Сессия открывается в фазе расчёта (Settlement) — решения допустимы только в Decision.
    session.AdvancePhase(PhaseTransitionTrigger.Facilitator);

    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(LmStudioClient.DefaultBaseUrl),
        Timeout = TimeSpan.FromMinutes(settings.TimeoutMinutes),
    };

    // Живой прогресс на экране (запрос пользователя 2026-08-16: "если токены идут — всё ок; если
    // не идут минуту — кажется всё сломалось"). Боты работают строго последовательно, поэтому в
    // любой момент идёт не больше одного запроса — эти два колбэка не нуждаются в привязке к
    // конкретному боту/ходу, достаточно "новый ответ начался" (token 1) и "давно тишина".
    var stallWarningThreshold = TimeSpan.FromSeconds(60);

    void OnToken(int count)
    {
        if (count == 1)
        {
            Console.WriteLine();
            stallWarningThreshold = TimeSpan.FromSeconds(60);
        }

        Console.Write($"\r  ...получено кусков ответа: {count}...   ");
        midStreamProgress = true;
    }

    void OnStalled(TimeSpan idle)
    {
        if (idle < stallWarningThreshold)
        {
            return;
        }

        Log($"  ⚠ нет новых кусков ответа уже {idle:mm\\:ss} — модель либо думает над трудным местом, либо зависла");
        stallWarningThreshold += TimeSpan.FromSeconds(30);
    }

    var llmClient = new LmStudioClient(httpClient, settings.Model, settings.Temperature, settings.MaxTokens, OnToken, OnStalled);

    var bots = teamIds
        .Select((id, i) => new LlmBot(id, personas[i % personas.Length], llmClient, settings.MaxAttempts))
        .ToList();

    // Файловый режим — каждая попытка (включая последнюю, пусть и неудачную) уходит на диск сразу
    // же, а не только при штатном завершении: если процесс упадёт или его убьют, наработанное не
    // теряется (запрос пользователя 2026-08-16).
    using var decisionLog = BotDecisionLog.CreateFile(settings.DecisionLogPath);
    using var metricsLog = BotMetricsLog.Create(settings.MetricsPath);
    var random = new Random();

    var runResult = await LlmBotSessionRunner.RunToCompletionAsync(
        session,
        bots,
        random,
        decisionLog,
        metricsLog,
        onTurnCompleted: s =>
        {
            foreach (var teamId in teamIds)
            {
                var team = s.State.Teams[teamId];
                Log($"  итог хода {s.State.CurrentTurn} — {team.Name}: баланс={team.Balance:0.00} " +
                    $"долг={team.Debt:0.00} netWorth={team.Balance - team.Debt:0.00} фабрик={team.Factories.Count}");
            }
        },
        onStatusLine: Log,
        maxConsecutiveExhaustedTurns: settings.MaxConsecutiveFailures);

    Log("");
    Log($"=== ОСТАНОВКА: {runResult.Reason} {runResult.Detail} ===");
    Log($"Финальный ход: {session.State.CurrentTurn}, сессия завершена: {session.State.IsFinished}");
    Log("");

    foreach (var teamId in teamIds)
    {
        var team = session.State.Teams[teamId];
        Log($"{team.Name} ИТОГ: баланс={team.Balance:0.00} долг={team.Debt:0.00} netWorth={team.Balance - team.Debt:0.00}");
        foreach (var factory in team.Factories)
        {
            Log($"  - {factory.Definition.Id} уровень={factory.Level} рабочих={factory.Workers}/{factory.DesiredWorkers} " +
                $"состояние={factory.Condition:0.00} рецепт={factory.SelectedRecipe.Id}");
        }
    }

    Log("");
    Log("Готово.");
}
catch (Exception ex)
{
    Log("");
    Log($"=== АВАРИЙНАЯ ОШИБКА: {ex} ===");
}

Log("");
Log("Нажмите любую клавишу, чтобы закрыть окно...");
Console.ReadKey();
