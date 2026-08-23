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

    public sealed record Result
    {
        public required GameSession Session { get; init; }
        public required IReadOnlyList<TeamTurnSnapshot> Snapshots { get; init; }
        public required IReadOnlyList<string> TraceLines { get; init; }
        public required IdealHallResult IdealHall { get; init; }
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

        var traceLines = new List<string>();
        var teams = new List<TeamSpec>();
        var bots = new List<SimpleBot>();
        foreach (var sector in config.Sectors)
        {
            for (var t = 0; t < teamsPerSector; t++)
            {
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"{sector.Id}-{t}", SectorId = sector.Id });
                bots.Add(new SimpleBot(teamId, sector, config, maintainFactories, leverage, profile, trace: traceLines.Add));
            }
        }

        var session = GameSession.StartWithEndTurn(config, presetId, endTurn, teams);
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
            IdealHall = idealHall,
        };
    }

    /// <summary>Та же разбивка по статьям расхода, что и <c>TraceRun.TraceCumulativeExpenses</c> — весь журнал заново на каждый ход (партия короткая, лишний проход не критичен, зато не пропустит источник и не задвоит).</summary>
    private static TeamTurnSnapshot BuildSnapshot(
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
