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
            var score = FinalScoreCalculator.Calculate(team, materialCosts, config.Raw.FactoryDefinitions).Score;
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
        // Чистый денежный поток команды по ходам (запрос пользователя, rebalance/2-sector-stepwise,
        // 2026-08-23: «важно видеть, зарабатывают они сейчас деньги или теряют, и сколько») — предыдущий
        // накопленный «доход минус все расходы», чтобы TraceCumulativeExpenses мог напечатать разницу
        // за конкретно ЭТОТ ход, не только итог с начала партии.
        var previousNetByTeam = new Dictionary<Ulid, decimal>();
        while (!session.State.IsFinished)
        {
            switch (session.State.CurrentPhase)
            {
                case TurnPhase.Settlement:
                    trace.Add($"=== TURN {session.State.CurrentTurn} ===");
                    var settled = session.RunTick(random);
                    TraceProductionAndSales(session, settled, trace);
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

                    TraceCumulativeExpenses(session, trace, previousNetByTeam);

                    session.AdvancePhase(PhaseTransitionTrigger.Timer);
                    break;
            }
        }
    }

    /// <summary>
    /// Доход и расходы команды по статьям, накопленным итогом с начала партии, плюс текущий баланс и
    /// денежный поток именно за ЭТОТ ход (доход минус все расходы этого хода — запрос пользователя,
    /// rebalance/2-sector-stepwise, 2026-08-23: «важно видеть, зарабатывают они сейчас деньги или
    /// теряют, и сколько», не только итоговый баланс под конец партии) — весь журнал (<see
    /// cref="GameSession.Entries"/>) перебирается заново на каждом ходу решений (простая, не
    /// инкрементальная реализация — журнал на масштабе одной партии-трассировки короткий, лишний
    /// проход не критичен), а не по частям на лету, чтобы не пропустить ни одного источника расхода и
    /// не задваивать (<see cref="FactoryProduced.LaborCost"/>, например, уже учтён в <see
    /// cref="SalariesPaid"/> — не включаю его отдельно, см. doc-comment <see cref="FactoryProduced"/>).
    /// <paramref name="previousNetByTeam"/> — накопленный «доход минус расход» с прошлого вызова, по
    /// команде; мутируется на выходе — тот же приём, что <see cref="SimpleBot"/> использует для
    /// <c>_previousNetWorth</c> в <c>UpdateFinancialTrend</c>.
    /// </summary>
    private static void TraceCumulativeExpenses(GameSession session, List<string> trace, Dictionary<Ulid, decimal> previousNetByTeam)
    {
        // Score, не только баланс (запрос пользователя, rebalance/2-sector-stepwise, 2026-08-23: «дальше
        // оперируем Score, по нему идёт оценка команд») — считается тем же способом, что и итоговый
        // Score в конце RunAsync, просто на каждом ходу, не только в самом конце партии.
        var materialCosts = MaterialCostCalculator.CalculateAll(session.State.Config);

        foreach (var team in session.State.Teams.Values.OrderBy(t => t.Sector.Id).ThenBy(t => t.Name))
        {
            decimal buildCost = 0m, hireFireCost = 0m, salary = 0m, upkeep = 0m, rnd = 0m, generation = 0m,
                overhaul = 0m, electricity = 0m, warehouseFee = 0m, emergencyPurchase = 0m, income = 0m;

            foreach (var entry in session.Entries)
            {
                switch (entry.Change)
                {
                    case FactoryBuilt c when c.TeamId == team.Id: buildCost += c.Cost; break;
                    case WorkersHired c when c.TeamId == team.Id: hireFireCost += c.Cost; break;
                    case WorkersFired c when c.TeamId == team.Id: hireFireCost += c.Cost; break;
                    case SalariesPaid c when c.TeamId == team.Id: salary += c.Amount; break;
                    case FactoryUpkeepPaid c when c.TeamId == team.Id: upkeep += c.Amount; break;
                    // Простой (вынужденный или во время капремонта) — зарплата/содержание по своему,
                    // отдельно зафиксированному тарифу простоя, не входят в SalariesPaid/FactoryUpkeepPaid
                    // выше (фабрики в простое сознательно исключены из общей командной кривой, см.
                    // doc-comment TickFinanceStep.Run) — без этой ветки сумма расходов не сходится с
                    // доходом-минус-балансом (нашли этим же способом, запрос пользователя).
                    case FactoryRepairTurnPassed c when c.TeamId == team.Id: salary += c.SalaryPaid; upkeep += c.UpkeepPaid; break;
                    case RndInvested c when c.TeamId == team.Id: rnd += c.Amount; break;
                    case GenerationResearchInvested c when c.TeamId == team.Id: generation += c.Amount; break;
                    case FactoryOverhaulStarted c when c.TeamId == team.Id: overhaul += c.Cost; break;
                    case FactoryProduced c when c.TeamId == team.Id: electricity += c.OverheadCost; break;
                    case WarehouseFeeCharged c when c.TeamId == team.Id: warehouseFee += c.Amount; break;
                    case EmergencyPurchased c when c.TeamId == team.Id: emergencyPurchase += c.TotalCost; break;
                    case MaterialSoldToSystem c when c.TeamId == team.Id: income += c.TotalRevenue; break;
                }
            }

            var totalExpense = buildCost + hireFireCost + salary + upkeep + rnd + generation + overhaul + electricity + warehouseFee + emergencyPurchase;
            var netCumulative = income - totalExpense;
            var cashFlowThisTurn = netCumulative - previousNetByTeam.GetValueOrDefault(team.Id);
            previousNetByTeam[team.Id] = netCumulative;
            var cashFlowText = cashFlowThisTurn >= 0 ? $"+{cashFlowThisTurn:F0}" : cashFlowThisTurn.ToString("F0");
            var score = FinalScoreCalculator.Calculate(team, materialCosts, session.State.Config.Raw.FactoryDefinitions).Score;

            trace.Add(
                $"{team.Name} накопленным итогом: доход={income:F0}, баланс={team.Balance:F0}, score={score:F0}, " +
                $"поток за ход={cashFlowText} | расходы: постройка={buildCost:F0}, " +
                $"наём/увольнение={hireFireCost:F0}, зарплата={salary:F0}, содержание={upkeep:F0}, R&D={rnd:F0}, " +
                $"поколение={generation:F0}, капремонт={overhaul:F0}, электричество={electricity:F0}, склад={warehouseFee:F0}, " +
                $"авар.закупка={emergencyPurchase:F0}");
        }
    }

    /// <summary>
    /// Что реально произвели фабрики и что реально продали системе за этот расчёт (не заявки из
    /// фазы решений — свершившийся факт, <see cref="FactoryProduced"/>/<see cref="MaterialSoldToSystem"/>
    /// из <see cref="GameSession.RunTick"/>) — по каждой команде и материалу, с суммой выручки. Продажа,
    /// заявленная в прошлую фазу решений, оседает как факт именно на СЛЕДУЮЩЕМ расчёте (SPEC §4) — тот
    /// же ход, где выпускается новая партия, поэтому обе строки печатаются рядом под одним заголовком
    /// хода, хотя формально относятся к разным фазам решений.
    /// </summary>
    private static void TraceProductionAndSales(GameSession session, IReadOnlyList<EventLogEntry<GameSessionState>> settled, List<string> trace)
    {
        foreach (var team in session.State.Teams.Values.OrderBy(t => t.Sector.Id).ThenBy(t => t.Name))
        {
            var produced = settled
                .Select(e => e.Change)
                .Select(c => c as FactoryProduced)
                .Where(c => c is not null && c.TeamId == team.Id)
                .GroupBy(c => team.Factories.Single(f => f.Id == c!.FactoryId).SelectedRecipe.Output.Id)
                .ToDictionary(g => g.Key, g => g.Sum(c => c!.OutputQuantity));
            var sold = settled
                .Select(e => e.Change)
                .Select(c => c as MaterialSoldToSystem)
                .Where(c => c is not null && c.TeamId == team.Id && c.Volume > 0)
                .GroupBy(c => c!.MaterialId)
                .ToDictionary(g => g.Key, g => (Volume: g.Sum(c => c!.Volume), Revenue: g.Sum(c => c!.TotalRevenue)));

            var producedText = produced.Count == 0 ? "-" : string.Join(", ", produced.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value:F1}"));
            var soldText = sold.Count == 0 ? "-" : string.Join(", ", sold.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value.Volume:F1}шт/{kv.Value.Revenue:F1}¤"));
            trace.Add($"{team.Name} произвели: {producedText}");
            trace.Add($"{team.Name} продали системе: {soldText}");
        }
    }

    /// <summary>Кладёт производный файл (<paramref name="fileName"/>) рядом с основным JSON-отчётом (<paramref name="outPath"/>) — та же папка, тот же приём, что уже есть у <c>--mode cost-levels</c>.</summary>
    private static string DerivePath(string outPath, string fileName)
    {
        var directory = Path.GetDirectoryName(outPath);
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }
}
