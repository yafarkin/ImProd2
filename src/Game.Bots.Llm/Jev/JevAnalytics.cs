using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm.Jev;

public sealed record JevResearchEstimate(decimal CommitmentPerTurn, int? TurnsToUnlock);
public sealed record JevInputEstimate(string Material, decimal RequiredPerTurn, decimal Stock,
    decimal NominalOwnSupply, decimal NominalExistingDemand, decimal NominalShortfall);
public sealed record JevDevelopmentEstimate(string Factory, string Recipe, string Output, int RequiredGeneration,
    decimal BuildCost, decimal HireCost, decimal ResearchInvestmentNeeded, bool Unlockable,
    IReadOnlyList<JevResearchEstimate> ResearchOptions, decimal NominalOutput, decimal SalaryPerTurn,
    decimal FixedCostPerTurn, decimal ElectricityPerTurn, decimal InputCostPerTurn,
    decimal RevenueAtCurrentQuote, decimal OperatingProfitWithFullSupply, decimal? SimplePaybackTurns,
    IReadOnlyList<JevInputEstimate> Inputs);
public sealed record JevFactoryEstimate(string Id, string Type, string Recipe, int DesiredWorkers,
    decimal Condition, bool UnderRepair, bool OverhaulRequested, decimal NominalOutput,
    decimal? SnapshotProfit, decimal? OverhaulCost, int? OverhaulDowntime,
    decimal NextConditionWithoutRepair, bool CriticalNextTurn);
public sealed record JevCashPoint(int Turn, decimal Balance, decimal ChangeSincePreviousDecision);
public sealed record JevAnalysis(int Turn, int MinimumRemainingTurns, int MaximumRemainingTurns,
    decimal Balance, int Generation, decimal RecurringResearch,
    IReadOnlyList<JevCashPoint> RecentCash, IReadOnlyList<JevFactoryEstimate> Factories,
    IReadOnlyList<JevDevelopmentEstimate> Development, object Market, string Assumptions);

/// <summary>Read-only estimates from visible state and public config. No model calls or game mutations.</summary>
public sealed class JevAnalytics(JevBotOptions options)
{
    public JevAnalysis Build(GameSession session, Ulid teamId)
    {
        options.Validate();
        var state = session.State;
        var team = state.Teams[teamId];
        var config = state.Config.Raw;
        var costs = MaterialCostCalculator.CalculateAll(state.Config);
        decimal Capacity(Factory f) => ProductionCalculator.CalculateCapacityBreakdown(f, f.DesiredWorkers, config.WorkerProductivity, config.Rnd).TheoreticalMaxOutput;
        decimal Demand(Material m) => team.Factories.Sum(f => f.SelectedRecipe.Inputs.Where(i => i.Material == m)
            .Sum(i => i.Quantity / f.SelectedRecipe.OutputQuantity * Capacity(f)));
        var development = new List<JevDevelopmentEstimate>();
        foreach (var definition in state.Config.FactoryDefinitions.Where(d => d.Sector == team.Sector)
            .OrderBy(d => d.Recipes[0].Output.Level).ThenBy(d => d.Id, StringComparer.Ordinal))
        foreach (var recipe in definition.Recipes.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (team.Factories.Any(f => f.Definition.Id == definition.Id && f.SelectedRecipe.Id == recipe.Id)) continue;
            var required = definition.Recipes[0].Output.Level;
            var research = config.GenerationResearch;
            var unlockable = required <= research.StartingGeneration + research.ResearchPointThresholdsByGeneration.Count;
            decimal investment = 0;
            if (required > team.UnlockedGeneration && unlockable)
            {
                var threshold = research.ResearchPointThresholdsByGeneration[required - research.StartingGeneration - 1];
                var total = decimal.Ceiling((decimal)Math.Pow((double)threshold, 1 / (double)research.DiminishingReturnsExponent));
                // Round conservatively; confirm against the engine's research calculation.
                while (GenerationResearchCalculator.CalculateResultingGeneration(team.UnlockedGeneration, total, research) < required) total++;
                investment = Math.Max(0, total - team.GenerationResearchInvestment);
            }
            var researchOptions = options.ResearchFractions.Select(f => f * research.MaxCommitmentPerTurn).Distinct().Order()
                .Select(amount => new JevResearchEstimate(amount, !unlockable ? null : investment == 0 ? 0 : amount == 0 ? null : (int)decimal.Ceiling(investment / amount))).ToArray();
            // Detached hypothetical factory used only by the pure capacity calculator.
            var hypothetical = new Factory(Ulid.Parse("00000000000000000000000001"), team.Sector, definition, recipe);
            var workers = config.WorkerProductivity.BaseWorkerCount;
            var output = ProductionCalculator.CalculateCapacityBreakdown(hypothetical, workers, config.WorkerProductivity, config.Rnd).TheoreticalMaxOutput;
            var raw = config.FactoryDefinitions.Single(d => d.Id == definition.Id);
            var inputs = recipe.Inputs.Select(i => new JevInputEstimate(i.Material.Id, output / recipe.OutputQuantity * i.Quantity,
                team.Warehouse.QuantityOf(i.Material), team.Factories.Where(f => f.SelectedRecipe.Output == i.Material).Sum(Capacity), Demand(i.Material),
                Math.Max(0, output / recipe.OutputQuantity * i.Quantity + Demand(i.Material) - team.Warehouse.QuantityOf(i.Material)
                    - team.Factories.Where(f => f.SelectedRecipe.Output == i.Material).Sum(Capacity)))).ToArray();
            var inputCost = inputs.Sum(i => i.RequiredPerTurn * costs.GetValueOrDefault(i.Material));
            var wage = workers * config.WorkerProductivity.SalaryPerWorkerPerTurn;
            var energy = output * state.Market.ElectricityPrice * config.Economy.ElectricityConsumptionPerOutputUnit;
            var quote = state.Market.QuoteOf(recipe.Output.Id);
            var revenue = (Math.Min(output, quote.Capacity) + Math.Max(0, output - quote.Capacity) * config.Economy.MarketCapacityOverflowDiscount) * quote.Price;
            var profit = revenue - inputCost - wage - raw.FixedCostPerTurn - energy;
            var hire = workers * config.WorkerProductivity.HireCostPerWorker;
            development.Add(new(definition.Id, recipe.Id, recipe.Output.Id, required, raw.BuildCost, hire, investment, unlockable,
                researchOptions, output, wage, raw.FixedCostPerTurn, energy, inputCost, revenue, profit,
                profit > 0 && unlockable ? (raw.BuildCost + hire + investment) / profit : null, inputs));
        }
        var factories = team.Factories.OrderBy(f => f.Id).Select(f =>
        {
            var raw = config.FactoryDefinitions.Single(d => d.Id == f.Definition.Id);
            var tier = f.IsUnderRepair || f.Condition >= 1 ? null : WearCalculator.SelectTier(f.Condition, config.Wear.OverhaulTiers);
            var age = WearCalculator.CalculateAgeBeyondGrace(f.LastResetTurn, state.CurrentTurn + 1, config.Wear.GracePeriodTurns);
            var next = WearCalculator.CalculateNextCondition(f.Condition, WearCalculator.CalculateDecayRate(age, config.Wear));
            decimal? profit = FactoryProfitabilityCalculator.TryCalculate(f, team.Factories, team.Warehouse, state.Market,
                config.WorkerProductivity, config.Rnd, out var estimate, raw.FixedCostPerTurn,
                config.Economy.ElectricityConsumptionPerOutputUnit, costs) ? estimate.Profit : null;
            return new JevFactoryEstimate(f.Id.ToString(), f.Definition.Id, f.SelectedRecipe.Id, f.DesiredWorkers,
                f.Condition, f.IsUnderRepair, f.OverhaulRequested, Capacity(f), profit,
                tier is null ? null : raw.BuildCost * tier.CostFraction, tier?.DurationTurns, next,
                !f.IsUnderRepair && !f.OverhaulRequested && WearCalculator.IsCritical(next, config.Wear));
        }).ToArray();
        return new(state.CurrentTurn, Math.Max(0, config.Duration.MinTurns - state.CurrentTurn), Math.Max(0, config.Duration.MaxTurns - state.CurrentTurn),
            team.Balance, team.UnlockedGeneration, team.GenerationResearchCommitmentPerTurn, CashHistory(session, teamId), factories,
            development.Take(options.MaxDevelopmentCandidates).ToArray(),
            state.Config.Materials.Values.Where(m => m.Sector == team.Sector && state.Market.HasQuote(m.Id)).OrderBy(m => m.Id, StringComparer.Ordinal)
                .Select(m => new { material = m.Id, stock = team.Warehouse.QuantityOf(m), quote = state.Market.QuoteOf(m.Id) }).ToArray(),
            "Development estimates assume base staffing, new condition, full input supply at calculated production cost and current quotes. " +
            "Payback includes build, hiring and remaining research, excludes ramp, repair, future prices and extra upstream capacity. " +
            "Input supply is a nominal upper bound, not a simulation; stock is one-off. Snapshot profit uses current workers and stock, " +
            "ignores same-tick upstream replenishment, pending hires and next wear. Cash changes include investment, not only operating results. " +
            "A missing payback is not evidence of profitability. No future random events or actual secret end turn are used.");
    }

    private IReadOnlyList<JevCashPoint> CashHistory(GameSession session, Ulid teamId)
    {
        var replay = new GameSessionState(session.State.Config);
        var points = new List<JevCashPoint>();
        decimal previous = 0;
        foreach (var entry in session.Entries)
        {
            entry.Change.Apply(replay);
            if (entry.Change is PhaseAdvanced && replay.CurrentPhase == TurnPhase.Decision && !replay.IsFinished)
            {
                var balance = replay.Teams[teamId].Balance;
                points.Add(new(replay.CurrentTurn, balance, balance - previous));
                previous = balance;
            }
        }
        return points.TakeLast(options.AnalyticsHistoryTurns).ToArray();
    }
}
