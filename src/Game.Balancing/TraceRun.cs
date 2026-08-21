using Game.Bots;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing;

/// <summary>
/// Одна партия (не сетка) с построчным логом решений — и идеального зала, и настоящих ботов
/// (Блок «трассировка ботов», rebalance/2-sector-stepwise) — в два отдельных текстовых файла рядом с
/// обычным JSON-отчётом. Нужен был для расследования обвала сходимости на кольце секторов: три
/// численных эксперимента вслепую (лаг доставки, ценовой потолок покупателя, деление рынка между
/// командами) ничего не дали, а пошаговое сравнение логов сразу показало, что за 16 ходов решений
/// не прошло ни одной сделки между командами — от этого и родился постоянный режим, не только для
/// того конкретного случая.
/// </summary>
internal static class TraceRun
{
    public static async Task RunAsync(ResolvedGameConfig config, CliArguments cliArguments)
    {
        var preset = config.Raw.SessionPresets.Single(p => p.Id == cliArguments.PresetId);

        var idealHallTraceLines = new List<string>();
        IdealHallCalculator.Trace = idealHallTraceLines.Add;
        var idealHall = IdealHallCalculator.Calculate(config, preset.MaxTurns);
        IdealHallCalculator.Trace = null;

        var idealHallTracePath = DerivePath(cliArguments.OutPath, "idealhall-trace.txt");
        await File.WriteAllLinesAsync(idealHallTracePath, idealHallTraceLines);
        Console.WriteLine($"Трассировка идеального зала записана: {Path.GetFullPath(idealHallTracePath)} ({idealHallTraceLines.Count} строк)");
        foreach (var branch in idealHall.Branches)
        {
            Console.WriteLine($"  X({preset.MaxTurns}) {branch.SectorId} = {branch.ValueByTurn[^1]:F0}");
        }

        var botTraceLines = new List<string>();
        var teams = new List<TeamSpec>();
        var bots = new List<SimpleBot>();
        foreach (var sector in config.Sectors)
        {
            for (var t = 0; t < cliArguments.TeamsPerSector; t++)
            {
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"{sector.Id}-{t}", SectorId = sector.Id });
                bots.Add(new SimpleBot(
                    teamId, sector, config, cliArguments.MaintainFactories,
                    cliArguments.Leverage, cliArguments.Profile, trace: botTraceLines.Add));
            }
        }

        Console.WriteLine($"Трассирую одну партию: leverage={cliArguments.Leverage:0.00}, profile={cliArguments.Profile:0.00}, команд на сектор={cliArguments.TeamsPerSector}.");

        // Ход окончания — детерминирован и равен preset.MaxTurns (тот же горизонт, что считает
        // IdealHallCalculator), не случайная жеребьёвка в [MinTurns, MaxTurns] — иначе Score(T) и X(T)
        // сравнивают разные T (запрос пользователя, rebalance/2-sector-stepwise, 2026-08-22).
        var session = GameSession.StartWithEndTurn(config, preset.Id, preset.MaxTurns, teams);
        var random = new Random(2);
        RunWithTrace(session, bots, random, botTraceLines);

        var botTracePath = DerivePath(cliArguments.OutPath, "bot-trace.txt");
        await File.WriteAllLinesAsync(botTracePath, botTraceLines);
        Console.WriteLine($"Трассировка ботов записана: {Path.GetFullPath(botTracePath)} ({botTraceLines.Count} строк)");
        var materialCosts = MaterialCostCalculator.CalculateAll(config);
        foreach (var team in session.State.Teams.Values.OrderBy(t => t.Sector.Id).ThenBy(t => t.Name))
        {
            var score = FinalScoreCalculator.Calculate(team, materialCosts, config.Raw.Economy, config.Raw.FactoryDefinitions).Score;
            Console.WriteLine($"  Score({preset.MaxTurns}) {team.Name} = {score:F0}");
        }
    }

    /// <summary>
    /// Тот же цикл ходов, что <see cref="BotSessionRunner.RunToCompletion"/> — здесь не переиспользован
    /// напрямую, потому что нужны дополнительные строки лога (граница хода, число сведённых сделок за
    /// ход), которых у самого решения ботов при вызове через колбэк не видно — сами решения бот пишет
    /// в лог сам (см. <see cref="SimpleBot"/>, параметр <c>trace</c> конструктора).
    /// </summary>
    private static void RunWithTrace(GameSession session, IReadOnlyList<SimpleBot> bots, Random random, List<string> trace)
    {
        var hasBuiltOut = false;
        while (!session.State.IsFinished)
        {
            switch (session.State.CurrentPhase)
            {
                case TurnPhase.Settlement:
                    trace.Add($"=== TURN {session.State.CurrentTurn} ===");
                    session.RunTick(random);
                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;

                case TurnPhase.Decision:
                    if (!hasBuiltOut)
                    {
                        foreach (var bot in bots) { bot.BuildOutSectorChain(session); }
                        hasBuiltOut = true;
                    }

                    foreach (var bot in bots)
                    {
                        bot.UpdateFinancialTrend(session);
                        bot.BuildNewlyUnlockedFactories(session);
                        bot.UpdateInvestmentPace(session);
                        bot.MaintainFactories(session);
                    }

                    var sellOrders = bots.SelectMany(bot => bot.ComputeSellOrders(session)).ToList();
                    var buyOrders = bots.SelectMany(bot => bot.ComputeBuyOrders(session)).ToList();
                    var contractsBefore = session.State.Contracts.Count;
                    OrderBook.Match(session, sellOrders, buyOrders, random);
                    var matched = session.State.Contracts.Count - contractsBefore;
                    trace.Add($"стакан: {sellOrders.Count} заявок на продажу, {buyOrders.Count} на покупку, сведено сделок: {matched}");

                    foreach (var bot in bots)
                    {
                        bot.SellSurplusToSystem(session);
                    }

                    foreach (var team in session.State.Teams.Values.OrderBy(t => t.Sector.Id).ThenBy(t => t.Name))
                    {
                        trace.Add($"баланс {team.Name}: {team.Balance:F0}");
                    }

                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;
            }
        }
    }

    /// <summary>Кладёт производный файл (<paramref name="fileName"/>) рядом с основным JSON-отчётом (<paramref name="outPath"/>) — та же папка, тот же приём, что уже есть у <c>--mode cost-levels</c>.</summary>
    private static string DerivePath(string outPath, string fileName)
    {
        var directory = Path.GetDirectoryName(outPath);
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }
}
