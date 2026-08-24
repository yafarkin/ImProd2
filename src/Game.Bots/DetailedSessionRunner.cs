using System.Text.RegularExpressions;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Прогоняет одну партию ботов и отдаёт структурированный, а не текстовый (как <c>--mode trace</c> в
/// <c>Game.Balancing</c>), построчный (по команде и по ходу) разбор решений — источник данных для
/// живой лаборатории баланса (<c>/admin/balance-lab</c>, Game.Web, TODO.md №28, rebalance/2-sector-stepwise,
/// 2026-08-23: «хочу сам играться с прогонами»). Разбивка расходов и денежный поток — та же формула,
/// что и построчный вывод <c>TraceRun.TraceCumulativeExpenses</c> (сознательно не переиспользуется
/// напрямую оттуда — тот метод пишет готовый текст в консоль, здесь нужны сырые числа для таблиц и
/// графиков; дублирование небольшое, ~20 строк, и снижает риск сломать уже проверенный CLI-инструмент
/// правкой сигнатуры общего метода).
/// </summary>
public static class DetailedSessionRunner
{
    /// <summary>Разбивка команды на конкретном ходу — накопленным итогом с начала партии, кроме <see cref="CashFlowThisTurn"/>.</summary>
    public sealed record TeamTurnSnapshot
    {
        public required int Turn { get; init; }
        public required Ulid TeamId { get; init; }
        public required string TeamName { get; init; }
        public required string SectorId { get; init; }
        public required decimal Income { get; init; }
        public required decimal Balance { get; init; }
        public required decimal Score { get; init; }

        /// <summary>Доход минус все расходы именно за этот ход (не накопленным итогом) — «сейчас зарабатывают или теряют».</summary>
        public required decimal CashFlowThisTurn { get; init; }
        public required decimal BuildCost { get; init; }
        public required decimal HireFireCost { get; init; }
        public required decimal Salary { get; init; }
        public required decimal Upkeep { get; init; }
        public required decimal Rnd { get; init; }
        public required decimal Generation { get; init; }
        public required decimal Overhaul { get; init; }
        public required decimal Electricity { get; init; }
        public required decimal WarehouseFee { get; init; }
        public required decimal EmergencyPurchase { get; init; }
    }

    /// <summary>
    /// Все семь категорий решений <see cref="SimpleBot"/> за один ход одной команды (см. doc-comment
    /// класса — запрос пользователя, 2026-08-23: «пошагово смотреть решения бота, какие цифры и
    /// мотивация»), с уже готовым для чтения текстом мотивации (то, что бот сам пишет в <c>_trace</c>).
    /// Пустой список — категория этот ход не сработала («нечего строить», «нечего продавать» и т.п.),
    /// не ошибка разбора.
    /// </summary>
    public sealed record TurnDecisionLog
    {
        public required int Turn { get; init; }
        public required string SectorId { get; init; }

        /// <summary>Троттлинг — см. <see cref="SimpleBot.UpdateFinancialTrend"/>.</summary>
        public required IReadOnlyList<string> FinancialTrend { get; init; }

        /// <summary>Что построено/пропущено — см. <see cref="SimpleBot.BuildNewlyUnlockedFactories"/>.</summary>
        public required IReadOnlyList<string> Build { get; init; }

        /// <summary>Темп R&amp;D/поколения — см. <see cref="SimpleBot.UpdateInvestmentPace"/>.</summary>
        public required IReadOnlyList<string> InvestmentPace { get; init; }

        /// <summary>Заказ капремонта — см. <see cref="SimpleBot.MaintainFactories"/>.</summary>
        public required IReadOnlyList<string> Overhaul { get; init; }

        /// <summary>Заявки на продажу другим командам — см. <see cref="SimpleBot.ComputeSellOrders"/>.</summary>
        public required IReadOnlyList<string> Sell { get; init; }

        /// <summary>Заявки на покупку у других команд — см. <see cref="SimpleBot.ComputeBuyOrders"/>.</summary>
        public required IReadOnlyList<string> Buy { get; init; }

        /// <summary>Продажа остатка системе — см. <see cref="SimpleBot.SellSurplusToSystem"/>.</summary>
        public required IReadOnlyList<string> SystemSale { get; init; }
    }

    public sealed record Result
    {
        public required GameSession Session { get; init; }
        public required IReadOnlyList<TeamTurnSnapshot> Snapshots { get; init; }
        public required IReadOnlyList<string> TraceLines { get; init; }
        public required IReadOnlyList<TurnDecisionLog> DecisionLogs { get; init; }
        public required IdealHallResult IdealHall { get; init; }
    }

    private static readonly Regex DecisionLinePattern = new(@"^\[([^\]]+)\] (.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Разбирает трассировку, уже помеченную ходом на момент записи (<see cref="Run"/> —
    /// <c>session.State.CurrentTurn</c> в момент вызова <c>_trace</c>; строк-заголовков вида
    /// <c>=== TURN N ===</c>, как у <c>--mode trace</c> в Game.Balancing, здесь никогда не было —
    /// тот текст пишет отдельный цикл в <c>TraceRun</c>, не <see cref="BotSessionRunner"/>, которым
    /// пользуется этот класс) в структурированный <see cref="TurnDecisionLog"/> по (ход, сектор) —
    /// только строки вида <c>[sectorId] ...</c> несут решение бота, остальные (заголовок хода,
    /// «произвели»/«продали системе»/«накопленным итогом», строка стакана) — это факты хода в целом,
    /// не решения конкретной команды, в разбор не идут (их по-прежнему видно в самом <see
    /// cref="Result.TraceLines"/>). Предполагает 1 команду на сектор — при нескольких у трассировки
    /// <see cref="SimpleBot"/> нет признака, к какой из них строка относится (только <c>Sector.Id</c>),
    /// различить нельзя.
    /// <c>internal</c>, не <c>private</c> — переиспользуется <see
    /// cref="InteractiveSessionRunner.ProposeManualTeamDecision"/> для той же разбивки трассировки
    /// одного пробного хода одной команды, не только целой партии.
    /// </summary>
    internal static IReadOnlyList<TurnDecisionLog> BuildDecisionLogs(IReadOnlyList<(int Turn, string Line)> taggedTraceLines)
    {
        var bySectorAndTurn = new Dictionary<(int Turn, string SectorId), (
            List<string> FinancialTrend, List<string> Build, List<string> InvestmentPace,
            List<string> Overhaul, List<string> Sell, List<string> Buy, List<string> SystemSale)>();

        foreach (var (turn, line) in taggedTraceLines)
        {
            var decisionMatch = DecisionLinePattern.Match(line);
            if (!decisionMatch.Success)
            {
                continue;
            }

            var sectorId = decisionMatch.Groups[1].Value;
            var text = decisionMatch.Groups[2].Value;
            var key = (turn, sectorId);
            if (!bySectorAndTurn.TryGetValue(key, out var bucket))
            {
                bucket = ([], [], [], [], [], [], []);
                bySectorAndTurn[key] = bucket;
            }

            var target = text switch
            {
                _ when text.StartsWith("throttle", StringComparison.Ordinal) => bucket.FinancialTrend,
                _ when text.StartsWith("строю", StringComparison.Ordinal) => bucket.Build,
                _ when text.StartsWith("пропускаю постройку", StringComparison.Ordinal) => bucket.Build,
                _ when text.StartsWith("темп вложений", StringComparison.Ordinal) => bucket.InvestmentPace,
                _ when text.StartsWith("заказываю капремонт", StringComparison.Ordinal) => bucket.Overhaul,
                _ when text.StartsWith("sellOrder", StringComparison.Ordinal) => bucket.Sell,
                _ when text.StartsWith("не продаю", StringComparison.Ordinal) => bucket.Sell,
                _ when text.StartsWith("buyOrder", StringComparison.Ordinal) => bucket.Buy,
                _ when text.StartsWith("не покупаю", StringComparison.Ordinal) => bucket.Buy,
                _ when text.StartsWith("продаю системе", StringComparison.Ordinal) => bucket.SystemSale,
                _ => null,
            };
            target?.Add(text);
        }

        return bySectorAndTurn
            .Select(pair => new TurnDecisionLog
            {
                Turn = pair.Key.Turn,
                SectorId = pair.Key.SectorId,
                FinancialTrend = pair.Value.FinancialTrend,
                Build = pair.Value.Build,
                InvestmentPace = pair.Value.InvestmentPace,
                Overhaul = pair.Value.Overhaul,
                Sell = pair.Value.Sell,
                Buy = pair.Value.Buy,
                SystemSale = pair.Value.SystemSale,
            })
            .OrderBy(log => log.Turn)
            .ThenBy(log => log.SectorId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Заводит <paramref name="teamsPerSector"/> ботов на каждый сектор конфига (детерминированное
    /// зерно жеребьёвки — тот же приём, что и <c>--mode trace</c>/<c>--mode diagnose</c>, чтобы повторный
    /// запуск с теми же параметрами давал тот же результат) и прогоняет партию до
    /// <paramref name="endTurn"/> хода целиком.
    /// </summary>
    public static Result Run(
        ResolvedGameConfig config, string presetId, int endTurn, int teamsPerSector,
        bool maintainFactories, decimal leverage, decimal profile)
    {
        ArgumentNullException.ThrowIfNull(config);

        var idealHall = IdealHallCalculator.Calculate(config, endTurn);

        // Команды и сессия — до ботов: строки трассировки нужно помечать ходом на момент записи
        // (session.State.CurrentTurn), поэтому колбэку _trace нужна уже живая сессия в замыкании, а
        // не голый список строк ( botTraceLines.Add) — иначе разобрать их обратно по ходам для
        // TurnDecisionLog нечем (в отличие от --mode trace, здесь никто не пишет "=== TURN N ===").
        var teamSectorPairs = new List<(TeamSpec Spec, Sector Sector)>();
        foreach (var sector in config.Sectors)
        {
            for (var t = 0; t < teamsPerSector; t++)
            {
                teamSectorPairs.Add((new TeamSpec { Id = Ulid.NewUlid(), Name = $"{sector.Id}-{t}", SectorId = sector.Id }, sector));
            }
        }

        var session = GameSession.StartWithEndTurn(config, presetId, endTurn, teamSectorPairs.Select(p => p.Spec).ToList());

        var traceLines = new List<string>();
        var taggedTraceLines = new List<(int Turn, string Line)>();
        void Trace(string line)
        {
            traceLines.Add(line);
            taggedTraceLines.Add((session.State.CurrentTurn, line));
        }

        var bots = teamSectorPairs
            .Select(p => new SimpleBot(p.Spec.Id, p.Sector, config, maintainFactories, leverage, profile, trace: Trace))
            .ToList();

        var materialCosts = MaterialCostCalculator.CalculateAll(config);
        var snapshots = new List<TeamTurnSnapshot>();
        var previousNetByTeam = new Dictionary<Ulid, decimal>();

        BotSessionRunner.RunToCompletion(session, bots, new Random(2), onTurnCompleted: _ =>
        {
            foreach (var team in session.State.Teams.Values)
            {
                snapshots.Add(BuildSnapshot(session, team, materialCosts, previousNetByTeam));
            }
        });

        return new Result
        {
            Session = session,
            Snapshots = snapshots,
            TraceLines = traceLines,
            DecisionLogs = BuildDecisionLogs(taggedTraceLines),
            IdealHall = idealHall,
        };
    }

    /// <summary>
    /// Та же разбивка по статьям расхода, что и <c>TraceRun.TraceCumulativeExpenses</c> — весь журнал
    /// заново на каждый ход (партия короткая, лишний проход не критичен, зато не пропустит источник и
    /// не задвоит). <c>internal</c>, не <c>private</c> — переиспользуется <see
    /// cref="InteractiveSessionRunner"/> для снимка ручной команды тем же способом.
    /// </summary>
    internal static TeamTurnSnapshot BuildSnapshot(
        GameSession session, Team team, IReadOnlyDictionary<string, decimal> materialCosts, Dictionary<Ulid, decimal> previousNetByTeam)
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
        var score = FinalScoreCalculator.Calculate(team, materialCosts, session.State.Config.Raw.FactoryDefinitions).Score;

        return new TeamTurnSnapshot
        {
            Turn = session.State.CurrentTurn,
            TeamId = team.Id,
            TeamName = team.Name,
            SectorId = team.Sector.Id,
            Income = income,
            Balance = team.Balance,
            Score = score,
            CashFlowThisTurn = cashFlowThisTurn,
            BuildCost = buildCost,
            HireFireCost = hireFireCost,
            Salary = salary,
            Upkeep = upkeep,
            Rnd = rnd,
            Generation = generation,
            Overhaul = overhaul,
            Electricity = electricity,
            WarehouseFee = warehouseFee,
            EmergencyPurchase = emergencyPurchase,
        };
    }
}
