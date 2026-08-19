using System.Globalization;
using System.Text;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>
/// Строит блок готовых, уже посчитанных показателей с трендом за окно последних ходов — прямой
/// запрос пользователя (2026-08-16): «маленькие модели плохо считают, но могут легче рассуждать над
/// готовыми цифрами». Отдельно от <see cref="BotStateSnapshotBuilder"/> (тот — сырой срез текущего
/// хода) и <see cref="BotHistorySeriesBuilder"/> (тот — сырой ряд точек по ходам, без свёртки в
/// «выросло/упало») — здесь ровно наоборот: не сырые числа, а уже сравненные окна с явным словом
/// тренда, чтобы не заставлять модель вычитать и делить в уме.
/// <para>
/// Все источники — уже существующие в движке calculators, ничего заново не выдумано: проценты/тело
/// кредита и денежный поток — из <see cref="FinanceHistoryCalculator"/> (та же бухгалтерская книга,
/// что питает вкладку «Финансы»), простой фабрик и загрузка — из <see cref="FactoryProduced"/>
/// (несёт готовое <c>CapacityLimitedOutputQuantity</c> отдельно от фактического
/// <c>OutputQuantity</c> — не нужно пересчитывать, было ли ограничение по сырью), рыночная позиция —
/// из <see cref="FactoryHistoryCalculator"/> (тот же ряд net worth, что уже строит «большой экран»).
/// Явно НЕ включает «потери на складе» как порчу материала — такой механики в движке нет вообще
/// (склад по SPEC не портится и не переполняется физически); вместо этого показана реальная плата за
/// перегруз склада (<see cref="FinanceHistoryCalculator.OperationType.WarehouseFee"/>) — тот же смысл
/// («у тебя лишнее на складе, это стоит денег»), но не выдуманная механика.
/// </para>
/// </summary>
public static class BotDerivedMetricsBuilder
{
    private enum Trend
    {
        Rising,
        Falling,
        Stable,
        Unknown,
    }

    /// <summary>Строит блок для команды <paramref name="teamId"/>; <paramref name="windowSize"/> — сколько последних ходов сравнивается с таким же по длине предыдущим окном.</summary>
    public static string Build(GameSession session, Ulid teamId, int windowSize = 5)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (windowSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), windowSize, "Must cover at least one turn.");
        }

        var state = session.State;
        if (!state.Teams.TryGetValue(teamId, out var team))
        {
            throw new ArgumentException($"Unknown team '{teamId}'.", nameof(teamId));
        }

        var recentWindow = (Start: Math.Max(1, state.CurrentTurn - windowSize + 1), End: state.CurrentTurn);
        (int Start, int End)? priorWindow = recentWindow.Start > 1
            ? (Math.Max(1, recentWindow.Start - windowSize), recentWindow.Start - 1)
            : null;

        var financeOps = FinanceHistoryCalculator.Summarize(session.Entries, state.Config, teamId);
        var productionPoints = CollectProductionPoints(session, teamId);

        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine(priorWindow is { } priorForHeader
            ? $"=== DERIVED METRICS (recent: turns {recentWindow.Start}-{recentWindow.End}, prior: turns {priorForHeader.Start}-{priorForHeader.End}) ==="
            : $"=== DERIVED METRICS (recent: turns {recentWindow.Start}-{recentWindow.End}, not enough history yet for a prior window) ===");

        AppendLoanService(text, financeOps, recentWindow, priorWindow);
        var recentNet = AppendCashFlow(text, financeOps, recentWindow, priorWindow);
        AppendWarehouseFee(text, financeOps, recentWindow, priorWindow);
        AppendIdleFactories(text, team, productionPoints);
        AppendUtilization(text, team, productionPoints, recentWindow, priorWindow);
        AppendRnd(text, team);
        AppendRunway(text, team, recentNet, recentWindow);
        AppendMarketPosition(text, session, teamId, priorWindow);

        return text.ToString();
    }

    private static void AppendLoanService(
        StringBuilder text, IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> ops,
        (int Start, int End) recent, (int Start, int End)? prior)
    {
        text.AppendLine();
        text.AppendLine("LOAN SERVICE");

        var recentInterest = SumOps(ops, recent, op => op.Type == FinanceHistoryCalculator.OperationType.InterestCharged);
        var recentPrincipal = SumOps(ops, recent, IsPrincipalRepayment);

        var interestLine = $"Interest paid: {Money(recentInterest)}";
        var principalLine = $"Principal repaid: {Money(recentPrincipal)}";

        if (prior is { } window)
        {
            var priorInterest = SumOps(ops, window, op => op.Type == FinanceHistoryCalculator.OperationType.InterestCharged);
            var priorPrincipal = SumOps(ops, window, IsPrincipalRepayment);
            interestLine += $" (prior {Money(priorInterest)}), trend: {TrendLabel(Classify(recentInterest, priorInterest))}";
            principalLine += $" (prior {Money(priorPrincipal)}), trend: {TrendLabel(Classify(recentPrincipal, priorPrincipal))}";
        }

        text.AppendLine(interestLine);
        text.AppendLine(principalLine);
    }

    private static bool IsPrincipalRepayment(FinanceHistoryCalculator.FinanceOperation op) =>
        op.Type is FinanceHistoryCalculator.OperationType.MandatoryRepayment or FinanceHistoryCalculator.OperationType.VoluntaryRepayment;

    /// <summary>Возвращает чистый денежный поток за <paramref name="recent"/> окно — переиспользуется в <see cref="AppendRunway"/>, чтобы не считать дважды.</summary>
    private static decimal AppendCashFlow(
        StringBuilder text, IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> ops,
        (int Start, int End) recent, (int Start, int End)? prior)
    {
        text.AppendLine();
        text.AppendLine("CASH FLOW");

        var (recentIncome, recentExpense) = SumIncomeExpense(ops, recent);
        var recentNet = recentIncome - recentExpense;
        var line = $"Net: {SignedMoney(recentNet)} (income {Money(recentIncome)}, expenses {Money(recentExpense)})";

        if (prior is { } window)
        {
            var (priorIncome, priorExpense) = SumIncomeExpense(ops, window);
            var priorNet = priorIncome - priorExpense;
            line += $" — prior net {SignedMoney(priorNet)}, trend: {TrendLabel(Classify(recentNet, priorNet))}";
        }

        text.AppendLine(line);
        return recentNet;
    }

    private static void AppendWarehouseFee(
        StringBuilder text, IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> ops,
        (int Start, int End) recent, (int Start, int End)? prior)
    {
        text.AppendLine();
        text.AppendLine("WAREHOUSE OVERAGE FEE (you are storing more than the free capacity — costs money, does not lose stock)");

        var recentFee = SumOps(ops, recent, op => op.Type == FinanceHistoryCalculator.OperationType.WarehouseFee);
        var line = Money(recentFee);

        if (prior is { } window)
        {
            var priorFee = SumOps(ops, window, op => op.Type == FinanceHistoryCalculator.OperationType.WarehouseFee);
            line += $" (prior {Money(priorFee)}), trend: {TrendLabel(Classify(recentFee, priorFee))}";
        }

        text.AppendLine(line);
    }

    private static void AppendIdleFactories(StringBuilder text, Team team, IReadOnlyList<FactoryProductionPoint> points)
    {
        text.AppendLine();
        text.AppendLine("IDLE / UNDERPERFORMING FACTORIES");

        if (team.Factories.Count == 0)
        {
            text.AppendLine("(no factories yet)");
            return;
        }

        var lastByFactory = points.GroupBy(p => p.FactoryId).ToDictionary(g => g.Key, g => g.Last());

        var any = false;
        foreach (var factory in team.Factories)
        {
            string? reason = null;
            if (factory.Workers == 0)
            {
                reason = "no workers assigned";
            }
            else if (factory.IsUnderRepair)
            {
                reason = $"under repair, {factory.RepairTurnsRemaining} turn(s) left";
            }
            else if (lastByFactory.TryGetValue(factory.Id, out var last) && last.Capacity > 0 && last.Output < last.Capacity * 0.95m)
            {
                reason = $"input material shortage (produced {Quantity(last.Output)} of {Quantity(last.Capacity)} possible)";
            }

            if (reason is null)
            {
                continue;
            }

            any = true;
            text.AppendLine($"- {factory.Definition.Id} (factoryId={factory.Id}): {reason}");
        }

        if (!any)
        {
            text.AppendLine("(none — all factories running at or near capacity)");
        }
    }

    private static void AppendUtilization(
        StringBuilder text, Team team, IReadOnlyList<FactoryProductionPoint> points,
        (int Start, int End) recent, (int Start, int End)? prior)
    {
        text.AppendLine();
        text.AppendLine("FACTORY UTILIZATION (actual output vs. capacity-limited potential, ignoring raw material limits)");

        if (team.Factories.Count == 0)
        {
            text.AppendLine("(no factories yet)");
            return;
        }

        var any = false;
        foreach (var factory in team.Factories)
        {
            var recentSum = SumWindow(points, factory.Id, recent);
            if (recentSum.Capacity <= 0)
            {
                continue; // ещё не производила в этом окне — не «загрузка», а «ещё не запускалась»
            }

            any = true;
            var recentUtilization = SafeDivide(recentSum.Output, recentSum.Capacity);
            var line = $"- {factory.Definition.Id} (factoryId={factory.Id}): {Percent(recentUtilization)}";

            if (prior is { } window)
            {
                var priorSum = SumWindow(points, factory.Id, window);
                if (priorSum.Capacity > 0)
                {
                    var priorUtilization = SafeDivide(priorSum.Output, priorSum.Capacity);
                    line += $" (prior {Percent(priorUtilization)}), trend: {TrendLabel(Classify(recentUtilization, priorUtilization))}";
                }
            }

            text.AppendLine(line);
        }

        if (!any)
        {
            text.AppendLine("(no production in the recent window yet)");
        }
    }

    private static void AppendRnd(StringBuilder text, Team team)
    {
        text.AppendLine();
        text.AppendLine("R&D");

        if (team.Factories.Count == 0)
        {
            text.AppendLine("(no factories yet)");
            return;
        }

        var totalCommitment = team.Factories.Sum(f => f.RndCommitmentPerTurn);
        var totalInvested = team.Factories.Sum(f => f.RndInvestment);
        text.AppendLine(
            $"Total factory R&D spend: {Money(totalCommitment)}/turn across {team.Factories.Count} factory(ies) " +
            $"(accumulated {Money(totalInvested)} invested so far).");
    }

    private static void AppendRunway(StringBuilder text, Team team, decimal recentNet, (int Start, int End) recentWindow)
    {
        text.AppendLine();
        text.AppendLine("RUNWAY");

        if (team.Balance <= 0)
        {
            text.AppendLine("Balance is already at or below zero.");
            return;
        }

        if (recentNet >= 0)
        {
            text.AppendLine("Not shrinking — recent net cash flow is zero or positive.");
            return;
        }

        var turnsInWindow = recentWindow.End - recentWindow.Start + 1;
        var avgPerTurn = recentNet / turnsInWindow;
        var runwayTurns = (int)Math.Ceiling(team.Balance / -avgPerTurn);
        text.AppendLine(
            $"At the recent net cash flow rate ({SignedMoney(avgPerTurn)}/turn), balance reaches zero in about " +
            $"{runwayTurns} turn(s) if nothing changes.");
    }

    private static void AppendMarketPosition(StringBuilder text, GameSession session, Ulid teamId, (int Start, int End)? prior)
    {
        var state = session.State;
        var own = state.Teams[teamId];
        var ownNetWorthNow = own.Balance - own.Debt;
        var leader = state.Teams.Values.OrderByDescending(t => t.Balance - t.Debt).First();

        text.AppendLine();
        text.AppendLine("MARKET POSITION (net worth = balance - debt)");

        if (leader.Id == teamId)
        {
            text.AppendLine($"You are currently the net worth leader ({Money(ownNetWorthNow)}).");
            return;
        }

        var leaderNetWorthNow = leader.Balance - leader.Debt;
        text.AppendLine(leaderNetWorthNow > 0
            ? $"Your net worth is {Percent(SafeDivide(ownNetWorthNow, leaderNetWorthNow))} of the leader's ({leader.Name}, {Money(leaderNetWorthNow)})."
            : $"You: {Money(ownNetWorthNow)}, leader ({leader.Name}): {Money(leaderNetWorthNow)} (both at or below zero).");

        if (prior is not { } window || leaderNetWorthNow <= 0)
        {
            return;
        }

        var ownHistory = FactoryHistoryCalculator.Summarize(session.Entries, state.Config, teamId);
        var leaderHistory = FactoryHistoryCalculator.Summarize(session.Entries, state.Config, leader.Id);
        var ownThen = ownHistory.NetWorthByTurn.LastOrDefault(p => p.Turn <= window.End);
        var leaderThen = leaderHistory.NetWorthByTurn.LastOrDefault(p => p.Turn <= window.End);

        if (ownThen == default || leaderThen == default || leaderThen.NetWorth <= 0)
        {
            return;
        }

        var thenRatio = SafeDivide(ownThen.NetWorth, leaderThen.NetWorth);
        var nowRatio = SafeDivide(ownNetWorthNow, leaderNetWorthNow);
        var trend = Classify(nowRatio, thenRatio) switch
        {
            Trend.Rising => "closing the gap",
            Trend.Falling => "falling further behind",
            Trend.Stable => "holding steady",
            _ => "n/a",
        };
        text.AppendLine($"Was {Percent(thenRatio)} of the leader's net worth around turn {window.End} — trend: {trend}.");
    }

    /// <summary>Реплеит журнал один раз, собирая (ход, фабрика, теоретический потолок, факт) для каждого <see cref="FactoryProduced"/> команды — тот же приём полного проигрывания, что у <see cref="FactoryHistoryCalculator"/>, но без лишних рядов, которые здесь не нужны.</summary>
    private static List<FactoryProductionPoint> CollectProductionPoints(GameSession session, Ulid teamId)
    {
        var scratch = new GameSessionState(session.State.Config);
        var points = new List<FactoryProductionPoint>();

        foreach (var entry in session.Entries)
        {
            entry.Change.Apply(scratch);
            if (entry.Change is FactoryProduced produced && produced.TeamId == teamId)
            {
                points.Add(new FactoryProductionPoint(scratch.CurrentTurn, produced.FactoryId, produced.CapacityLimitedOutputQuantity, produced.OutputQuantity));
            }
        }

        return points;
    }

    private static (decimal Capacity, decimal Output) SumWindow(IReadOnlyList<FactoryProductionPoint> points, Ulid factoryId, (int Start, int End) window)
    {
        decimal capacity = 0m, output = 0m;
        foreach (var point in points)
        {
            if (point.FactoryId != factoryId || point.Turn < window.Start || point.Turn > window.End)
            {
                continue;
            }

            capacity += point.Capacity;
            output += point.Output;
        }

        return (capacity, output);
    }

    private static decimal SumOps(
        IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> ops, (int Start, int End) window,
        Func<FinanceHistoryCalculator.FinanceOperation, bool> predicate)
    {
        decimal total = 0m;
        foreach (var op in ops)
        {
            if (op.Turn >= window.Start && op.Turn <= window.End && predicate(op))
            {
                total += op.Amount;
            }
        }

        return total;
    }

    private static (decimal Income, decimal Expense) SumIncomeExpense(IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> ops, (int Start, int End) window)
    {
        decimal income = 0m, expense = 0m;
        foreach (var op in ops)
        {
            if (op.Turn < window.Start || op.Turn > window.End)
            {
                continue;
            }

            if (op.Direction == FinanceHistoryCalculator.MoneyDirection.Income)
            {
                income += op.Amount;
            }
            else
            {
                expense += op.Amount;
            }
        }

        return (income, expense);
    }

    /// <summary>Порог 10% относительного изменения от предыдущего окна — меньше считается «стабильно», не шумом округления.</summary>
    private static Trend Classify(decimal recent, decimal prior)
    {
        var baseline = Math.Abs(prior);
        if (baseline < 0.01m)
        {
            if (Math.Abs(recent) < 0.01m)
            {
                return Trend.Stable;
            }

            return recent > prior ? Trend.Rising : Trend.Falling;
        }

        var relativeChange = (recent - prior) / baseline;
        if (relativeChange > 0.1m)
        {
            return Trend.Rising;
        }

        if (relativeChange < -0.1m)
        {
            return Trend.Falling;
        }

        return Trend.Stable;
    }

    private static string TrendLabel(Trend trend) => trend switch
    {
        Trend.Rising => "rising",
        Trend.Falling => "falling",
        Trend.Stable => "stable",
        _ => "n/a",
    };

    private static decimal SafeDivide(decimal numerator, decimal denominator) => denominator == 0m ? 0m : numerator / denominator;

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string SignedMoney(decimal value) => (value >= 0 ? "+" : "") + Money(value);

    private static string Quantity(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Percent(decimal fraction) => (fraction * 100m).ToString("0", CultureInfo.InvariantCulture) + "%";

    private readonly record struct FactoryProductionPoint(int Turn, Ulid FactoryId, decimal Capacity, decimal Output);
}
