using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Game.Bots;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Game.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.LoadTest;

/// <summary>
/// Блок 10.4 (BUILD_PLAN, SPEC §14) — локальная замена «нагрузочного теста на реальной Raspberry
/// Pi»: без самого RPi и без headless-браузера в масштабе ~25 клиентов (см. план блока в
/// <c>docs/BUILD_PLAN.md</c>). Фаза A меряет производительность ядра игры, Фаза B — конкурентность
/// веб-слоя поверх <see cref="WebApplicationFactory{TEntryPoint}"/>. Обе цифры — ориентир для
/// разработки, не замена проверки на боевом железе перед пилотом. Класс называется не
/// <c>Program</c> нарочно: <c>Game.Web</c> — top-level-statement проект и уже даёт публичный
/// частичный класс <c>Program</c> в глобальном пространстве имён; при ссылке на него отсюда
/// собственный top-level <c>Program</c> этого проекта конфликтовал бы с ним по имени.
/// </summary>
internal static class LoadTestRunner
{
    private static async Task Main()
    {
        RunTickBenchmark();
        await RunHttpSmokeAsync();
    }

    private static void RunTickBenchmark()
    {
        Console.WriteLine("=== Фаза A: бенчмарк тика движка (Game.Engine, in-process) ===");

        var configPath = Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");
        var config = GameConfigLoader.LoadFromFile(configPath);
        var preset = config.Raw.SessionPresets.Single(p => p.Id == "short");
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");
        var budgetMs = config.Raw.PhaseTiming.CalculationPhaseSeconds * 1000d;

        const int sessionCount = 10;
        var tickDurations = new List<(int Turn, double ElapsedMs)>();

        for (var i = 0; i < sessionCount; i++)
        {
            var teams = new List<TeamSpec>();
            var bots = new List<SimpleBot>();
            for (var t = 0; t < 8; t++)
            {
                var sector = t % 2 == 0 ? sectorA : sectorB;
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"Бот {t}", SectorId = sector.Id, StartingLoanAmount = 10_000m });
                bots.Add(new SimpleBot(teamId, sector, config));
            }

            var contractPairs = PairBySector(bots);
            var session = GameSession.Start(config, preset, teams, new Random(i + 1));
            var random = new Random(i + 1_000_000);
            var hasBuiltOut = false;

            while (!session.State.IsFinished)
            {
                switch (session.State.CurrentPhase)
                {
                    case TurnPhase.Calculation:
                        var stopwatch = Stopwatch.StartNew();
                        session.RunTick(random);
                        stopwatch.Stop();
                        tickDurations.Add((session.State.CurrentTurn, stopwatch.Elapsed.TotalMilliseconds));
                        session.AdvancePhase(PhaseTransitionTrigger.Timer);
                        break;

                    case TurnPhase.Decision:
                        if (!hasBuiltOut)
                        {
                            foreach (var bot in bots)
                            {
                                bot.BuildOutSectorChain(session);
                            }
                            hasBuiltOut = true;
                        }
                        foreach (var (seller, buyer) in contractPairs)
                        {
                            SimpleBot.TrySignSimpleContract(session, seller, buyer, random);
                        }
                        foreach (var bot in bots)
                        {
                            bot.SellSurplusToSystem(session);
                        }
                        session.AdvancePhase(PhaseTransitionTrigger.Timer);
                        break;

                    case TurnPhase.Closing:
                        session.AdvancePhase(PhaseTransitionTrigger.Timer);
                        break;
                }
            }
        }

        var ordered = tickDurations.Select(t => t.ElapsedMs).OrderBy(ms => ms).ToList();

        Console.WriteLine($"Тиков посчитано: {ordered.Count} (партий: {sessionCount}, 8 команд/партия, бюджет фазы расчёта: {budgetMs:N0} мс).");
        Console.WriteLine($"Мин: {ordered.Min():N1} мс | Среднее: {ordered.Average():N1} мс | P95: {Percentile(ordered, 0.95):N1} мс | Макс: {ordered.Max():N1} мс");

        var overBudget = tickDurations.Count(t => t.ElapsedMs > budgetMs * 0.2);
        if (overBudget > 0)
        {
            Console.WriteLine($"ВНИМАНИЕ: {overBudget} тик(ов) превысили 20% бюджета фазы расчёта.");
        }

        const string csvPath = "loadtest-tick-times.csv";
        using (var writer = new StreamWriter(csvPath))
        {
            writer.WriteLine("Turn,ElapsedMs");
            foreach (var (turn, elapsedMs) in tickDurations)
            {
                writer.WriteLine(string.Join(',',
                    turn.ToString(CultureInfo.InvariantCulture),
                    elapsedMs.ToString(CultureInfo.InvariantCulture)));
            }
        }

        Console.WriteLine($"CSV с длительностями тиков записан: {Path.GetFullPath(csvPath)}");
        Console.WriteLine("Измерено на этой машине — Raspberry Pi 4 медленнее, перед пилотом обязательно перегнать на боевом железе.");
        Console.WriteLine();
    }

    private static async Task RunHttpSmokeAsync()
    {
        Console.WriteLine("=== Фаза B: HTTP-смоук через параллельных клиентов (WebApplicationFactory) ===");
        Console.WriteLine("Ограничение: TestServer — не настоящие сокеты Kestrel, не сам SignalR-протокол Blazor-цепи.");
        Console.WriteLine("Ловит конкурентные баги (deadlock/исключения) и даёт базовую задержку по страницам,");
        Console.WriteLine("не заменяет ручную проверку кликов в браузере (см. чек-лист пилота).");
        Console.WriteLine();

        // WebApplicationFactory нормально резолвит content root Game.Web автоматически только под
        // тестовым хостом (dotnet test/VSTest); под `dotnet run` он падает на наивную догадку
        // «текущий каталог + имя сборки» без «src/» — задаём content root явно.
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseContentRoot(FindGameWebContentRoot()));
        var host = factory.Services.GetRequiredService<GameSessionHost>();

        var config = host.TrainingConfig;
        var preset = config.Raw.SessionPresets.Single(p => p.Id == "training");
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");

        var teams = new List<TeamSpec>();
        for (var t = 0; t < 8; t++)
        {
            var sector = t % 2 == 0 ? sectorA : sectorB;
            teams.Add(new TeamSpec { Id = Ulid.NewUlid(), Name = $"Команда {t + 1}", SectorId = sector.Id, StartingLoanAmount = 10_000m });
        }

        host.StartNewSession(config, preset, teams);

        var participants = new List<(string Code, string Route)>();
        foreach (var team in teams)
        {
            participants.Add((Register(host, ParticipantRole.Manager, team.Id, $"Управляющий {team.Name}"), "/team"));
            participants.Add((Register(host, ParticipantRole.Negotiator, team.Id, $"Переговорщик 1 {team.Name}"), "/team"));
            participants.Add((Register(host, ParticipantRole.Negotiator, team.Id, $"Переговорщик 2 {team.Name}"), "/team/negotiate"));
        }
        participants.Add((Register(host, ParticipantRole.Operator, null, "Оператор"), "/operator"));
        participants.Add((Register(host, ParticipantRole.Facilitator, null, "Ведущий"), "/facilitator"));
        participants.Add((Register(host, ParticipantRole.Administrator, null, "Администратор"), "/admin"));

        // StartNewSession сразу ставит сессию на паузу (даёт администратору время на регистрацию
        // участников без расхода игрового времени, см. doc-comment GameSessionHost.StartNewSession)
        // — без явного Resume фоновый таймер её никогда не тронет.
        lock (host.SyncRoot)
        {
            host.Session!.Resume();
        }

        Console.WriteLine($"Сессия запущена: {teams.Count} команд, {participants.Count} именованных участников + 1 анонимный (большой экран).");

        var stats = new ConcurrentDictionary<string, ConcurrentBag<(double Ms, int Status)>>();
        var tasks = participants
            .Select(p => PollAsAsync(factory, host, p.Code, p.Route, stats))
            .Append(PollAnonymouslyAsync(factory, host, "/screen", stats))
            .ToList();

        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine($"Прогон завершён за {stopwatch.Elapsed:mm\\:ss}. Ход {host.Session!.State.CurrentTurn}, сессия {(host.Session.State.IsFinished ? "завершена" : "НЕ завершена")}.");
        Console.WriteLine();
        Console.WriteLine("Маршрут            Запросов  Ошибок  Мин(мс)  Сред(мс)  P95(мс)  Макс(мс)");
        foreach (var (route, samples) in stats.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var ms = samples.Select(s => s.Ms).OrderBy(x => x).ToList();
            var errors = samples.Count(s => s.Status != 200);
            Console.WriteLine($"{route,-18} {ms.Count,9} {errors,7} {ms.Min(),8:N0} {ms.Average(),9:N0} {Percentile(ms, 0.95),8:N0} {ms.Max(),8:N0}");
        }
    }

    private static string FindGameWebContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ImProd.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException($"Solution root (ImProd.sln) not found above '{AppContext.BaseDirectory}'.");
        }

        return Path.Combine(directory.FullName, "src", "Game.Web");
    }

    private static string Register(GameSessionHost host, ParticipantRole role, Ulid? teamId, string displayName)
    {
        var entry = host.RegisterParticipant(role, teamId, displayName);
        return ((ParticipantRegistered)entry.Change).Code;
    }

    private static async Task PollAsAsync(
        WebApplicationFactory<Program> factory,
        GameSessionHost host,
        string code,
        string route,
        ConcurrentDictionary<string, ConcurrentBag<(double Ms, int Status)>> stats)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code }));
        await PollLoopAsync(client, host, route, stats);
    }

    private static async Task PollAnonymouslyAsync(
        WebApplicationFactory<Program> factory,
        GameSessionHost host,
        string route,
        ConcurrentDictionary<string, ConcurrentBag<(double Ms, int Status)>> stats)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await PollLoopAsync(client, host, route, stats);
    }

    private static async Task PollLoopAsync(
        HttpClient client,
        GameSessionHost host,
        string route,
        ConcurrentDictionary<string, ConcurrentBag<(double Ms, int Status)>> stats)
    {
        var bag = stats.GetOrAdd(route, _ => new ConcurrentBag<(double, int)>());

        while (true)
        {
            lock (host.SyncRoot)
            {
                if (host.Session is null || host.Session.State.IsFinished)
                {
                    return;
                }
            }

            var stopwatch = Stopwatch.StartNew();
            var response = await client.GetAsync(route);
            stopwatch.Stop();
            bag.Add((stopwatch.Elapsed.TotalMilliseconds, (int)response.StatusCode));

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private static List<(SimpleBot Seller, SimpleBot Buyer)> PairBySector(IReadOnlyList<SimpleBot> bots)
    {
        var pairs = new List<(SimpleBot, SimpleBot)>();
        foreach (var sectorBots in bots.GroupBy(bot => bot.Sector.Id).OrderBy(group => group.Key))
        {
            var ordered = sectorBots.OrderBy(bot => bot.TeamId).ToList();
            for (var i = 0; i + 1 < ordered.Count; i += 2)
            {
                pairs.Add((ordered[i], ordered[i + 1]));
            }
        }

        return pairs;
    }

    private static double Percentile(IReadOnlyList<double> ordered, double p) =>
        ordered[(int)Math.Clamp(Math.Round(p * (ordered.Count - 1)), 0, ordered.Count - 1)];
}
