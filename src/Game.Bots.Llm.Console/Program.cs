// Автономный прогон LLM-ботов (запрос пользователя 2026-08-16): собрать, запустить под Windows и
// уйти спать — устойчивый к отдельным сбоям (щедрые таймауты и ретраи, остановка сессии целиком
// только при явно застрявшем боте), с построчным статусом на экран в реальном времени ("бот 2, ход
// 14, запрос к LLM...", "бот 2, ход 14, TakeLoan за 03:12"), плюс CSV-метрики и сырой JSONL-лог
// решений на диск — не только на экран, переживает закрытие консоли. Production-модель и раскладка
// ботов по секторам (запрос пользователя 2026-08-20: стадия 2, два бота, металлургия +
// нефтегазохимия, для обкатки межсекторной доски заявок) — через RunSettings.ProductionModel/
// Sectors, не хардкод; см. run-llm-bots-stage1.sh против run-llm-bots-stage2.sh.
//
// Запрос пользователя 2026-08-19: пережить Ctrl+C/убийство процесса и на следующем запуске
// продолжить с того же места, не с начала. Игровое состояние — через
// Game.Persistence.DurableEventLog (та же durable-обёртка, что уже проверена в Game.Web для
// восстановления сессии после сбоя): каждое событие дописывается на диск сразу же при исполнении,
// восстановление — снапшот (если есть) + доигрывание хвоста журнала. Всё, что журналом НЕ
// покрывается — пути файлов этого прогона (чтобы дописывать те же самые, не плодить новые с новой
// меткой времени), сид Random (сам поток чисел после возобновления не совпадёт с гипотетическим
// непрерывным прогоном — для качественного плейтеста не важно) и собственная память каждого бота
// (BotTurnHistory, не часть игрового журнала) — лежит в BotRunCheckpoint (".working.json" по
// умолчанию), переписывается целиком после каждого хода, удаляется по чистой остановке.

using System.Text;
using Game.Bots.Llm;
using Game.Bots.Llm.ConsoleApp;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Game.Persistence;

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

var checkpoint = BotRunCheckpoint.TryLoad(settings.CheckpointPath);
var sectors = settings.Sectors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (sectors.Length == 0)
{
    sectors = ["A"];
}

Log("=== LLM-боты, автономный прогон ===");
Log($"Production-модель: {settings.ProductionModel}, секторы ботов (по кругу): {string.Join(",", sectors)}");
Log($"LM Studio: {LmStudioClient.DefaultBaseUrl}");
Log($"Модель: {settings.Model}, температура: {settings.Temperature}, max_tokens: {settings.MaxTokens}, " +
    $"thinking отключён: {settings.DisableThinking}");
Log($"Ботов: {settings.BotCount}, ходов до конца сессии: {settings.Turns}");
Log($"HTTP-таймаут запроса: {settings.TimeoutMinutes} мин, попыток на ход (один вызов LLM на ход): {settings.MaxAttempts}, " +
    $"потолок действий в массиве за ход: {settings.MaxActionsPerTurn}, " +
    $"остановка после {settings.MaxConsecutiveFailures} провалов подряд у одного бота");
Log($"Лог на диске: {logPath}");
if (checkpoint is not null)
{
    Log($"Найден чекпойнт прерванного прогона ({settings.CheckpointPath}) — продолжаю его, не начинаю заново.");
    Log($"Метрики: {checkpoint.MetricsPath}");
    Log($"Сырой лог решений: {checkpoint.DecisionLogPath}");
}
else
{
    Log($"Метрики: {settings.MetricsPath}");
    Log($"Сырой лог решений: {settings.DecisionLogPath}");
}
Log("");

Console.CancelKeyPress += (_, _) => Log(
    "⚠ Получен Ctrl+C — журнал сессии и чекпойнт уже на диске (пишутся синхронно на каждом ходу), " +
    "следующий запуск этого же .bat/.sh продолжит с последнего завершённого хода.");

try
{
    var productionModelPath = Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", settings.ProductionModel);
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

    var llmClient = new LmStudioClient(
        httpClient, settings.Model, settings.Temperature, settings.MaxTokens, OnToken, OnStalled, settings.DisableThinking,
        settings.MaxActionsPerTurn);

    // Пути ЭТОГО прогона — из чекпойнта при возобновлении (те же файлы, дозаписываем), иначе новые
    // с меткой времени (см. RunSettings).
    string journalPath, snapshotPath, metricsPath, decisionLogPath;
    GameSession session;
    List<Ulid> teamIds;
    List<LlmBot> bots;
    Random random;
    int randomSeed;

    if (checkpoint is not null)
    {
        journalPath = checkpoint.JournalPath;
        snapshotPath = checkpoint.SnapshotPath;
        metricsPath = checkpoint.MetricsPath;
        decisionLogPath = checkpoint.DecisionLogPath;

        if (checkpoint.Bots.Count != settings.BotCount)
        {
            Log($"⚠ В чекпойнте {checkpoint.Bots.Count} бот(ов), а LLM_BOT_COUNT сейчас {settings.BotCount} — " +
                "продолжаю с числом ботов из чекпойнта, не трогайте LLM_BOT_COUNT между запусками одного прогона.");
        }

        var durableLog = DurableEventLog<GameSessionState>.Open(journalPath, snapshotPath, () => new GameSessionState(config));
        session = new GameSession(durableLog);
        randomSeed = checkpoint.RandomSeed;
        random = new Random(randomSeed);

        teamIds = checkpoint.Bots.Select(b => Ulid.Parse(b.TeamId)).ToList();
        bots = checkpoint.Bots
            .Select(b => new LlmBot(
                Ulid.Parse(b.TeamId), personas[b.PersonaIndex % personas.Length], llmClient, settings.MaxAttempts,
                maxActionsPerTurn: settings.MaxActionsPerTurn, initialHistory: b.History))
            .ToList();

        Log($"Восстановлено: ход {session.State.CurrentTurn}, фаза {session.State.CurrentPhase}, {teamIds.Count} бот(ов).");
    }
    else
    {
        journalPath = settings.JournalPath;
        snapshotPath = settings.SnapshotPath;
        metricsPath = settings.MetricsPath;
        decisionLogPath = settings.DecisionLogPath;

        var teamSpecs = new List<TeamSpec>();
        teamIds = new List<Ulid>();
        for (var i = 0; i < settings.BotCount; i++)
        {
            var id = Ulid.NewUlid();
            teamIds.Add(id);
            teamSpecs.Add(new TeamSpec { Id = id, Name = $"Бот {i + 1}", SectorId = sectors[i % sectors.Length] });
        }

        var durableLog = DurableEventLog<GameSessionState>.Open(journalPath, snapshotPath, () => new GameSessionState(config));
        session = GameSession.StartWithEndTurn(durableLog, "full", settings.Turns, teamSpecs);
        // Сессия открывается в фазе расчёта (Settlement) — решения допустимы только в Decision.
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);

        // Не фиксированный сид — но сохраняется в чекпойнт ниже, чтобы возобновление хотя бы имело
        // повторяемый (пусть и не идентичный прерванному прогону) поток чисел вперёд от точки
        // возобновления, а не новый случайный каждый раз.
        randomSeed = Random.Shared.Next();
        random = new Random(randomSeed);

        bots = teamIds
            .Select((id, i) => new LlmBot(
                id, personas[i % personas.Length], llmClient, settings.MaxAttempts, maxActionsPerTurn: settings.MaxActionsPerTurn))
            .ToList();
    }

    // Файловый режим — каждая попытка (включая последнюю, пусть и неудачную) уходит на диск сразу
    // же, а не только при штатном завершении: если процесс упадёт или его убьют, наработанное не
    // теряется (запрос пользователя 2026-08-16). CreateFile/Create дозаписывают существующий файл,
    // не дублируя заголовок — безопасно вызывать на путях из чекпойнта тоже (запрос пользователя
    // 2026-08-19).
    using var decisionLog = BotDecisionLog.CreateFile(decisionLogPath);
    using var metricsLog = BotMetricsLog.Create(metricsPath);

    void SaveCheckpoint()
    {
        var entry = new BotRunCheckpoint(
            RandomSeed: randomSeed,
            LogPath: logPath,
            MetricsPath: metricsPath,
            DecisionLogPath: decisionLogPath,
            JournalPath: journalPath,
            SnapshotPath: snapshotPath,
            Bots: bots.Select((bot, i) => new BotCheckpointEntry(bot.TeamId.ToString(), i, bot.History)).ToList());
        entry.Save(settings.CheckpointPath);
    }

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

            SaveCheckpoint();
        },
        onStatusLine: Log,
        maxConsecutiveExhaustedTurns: settings.MaxConsecutiveFailures);

    // Обе причины остановки здесь — осознанные, не сбой: чекпойнт больше не нужен, следующий запуск
    // должен начать новый прогон, а не донашивать этот же (запрос пользователя 2026-08-19: удалять
    // по завершению). Прерывание Ctrl+C/убийство процесса до этой строки чекпойнт не тронет.
    File.Delete(settings.CheckpointPath);

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
    Log("Чекпойнт (если он был создан) оставлен на диске — следующий запуск попробует продолжить с последнего сохранённого хода.");
}

Log("");
Log("Нажмите любую клавишу, чтобы закрыть окно...");
Console.ReadKey();
