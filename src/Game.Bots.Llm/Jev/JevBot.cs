using System.Diagnostics;
using System.Text.Json;
using Game.Engine;

namespace Game.Bots.Llm.Jev;

public sealed record JevDecisionRecord(int Turn, int Step, JevRequest Request, string? RawResponse,
    string Selected, BotCommand Command, string Outcome, double ElapsedMilliseconds);

public sealed class JevBot(IJevClient client, JevBotOptions options)
{
    /// <returns>True when this turn ended because of a service or command failure.</returns>
    public async Task<bool> TakeTurnAsync(GameSession session, Ulid teamId, Random random,
        Func<JevDecisionRecord, Task> writeLog, CancellationToken cancellationToken = default, Func<JevPlanningRecord, Task>? writePlanningLog = null)
    {
        options.Validate();
        JevPlan? plan = null;
        if (options.UsePlanning)
        {
            var analysis = new JevAnalytics(options).Build(session, teamId);
            var planningRequest = JevPlanning.Create(AssistedState(session, analysis), analysis);
            var planningWatch = Stopwatch.StartNew();
            try
            {
                plan = await client.AssessAsync(planningRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                var raw = (ex as JevResponseException)?.RawResponse;
                var outcome = $"fallback: {ex.GetType().Name}: {ex.Message}";
                if (writePlanningLog is not null)
                    await writePlanningLog(new(session.State.CurrentTurn, planningRequest, null, raw, outcome, planningWatch.Elapsed.TotalMilliseconds)).ConfigureAwait(false);
                return true;
            }
            if (writePlanningLog is not null)
                await writePlanningLog(new(session.State.CurrentTurn, planningRequest, plan, plan.RawResponse, "success", planningWatch.Elapsed.TotalMilliseconds)).ConfigureAwait(false);
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var step = 0; step < options.MaxActionsPerTurn; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = new JevActionCatalog(options).Build(session, teamId)
                .Where(a => !used.Contains(Slot(a))).ToDictionary(a => a.Id, StringComparer.Ordinal);
            var observation = options.UseAnalytics ? AssistedState(session, new JevAnalytics(options).Build(session, teamId)) : BuildState(session, teamId);
            object state = plan is null ? observation : new
            {
                observation,
                earlierAssessment = new { plan.Priority, plan.TargetRecipe, plan.ResearchNowProbability },
                note = "This assessment was made at turn start. Reconsider if completed actions or current numbers change the situation.",
            };
            var request = new JevRequest(state, candidates.ToDictionary(a => a.Key, a => a.Value.Description, StringComparer.Ordinal));
            var watch = Stopwatch.StartNew();
            string? raw = null;
            var selected = "finish";
            string? failure = null;
            try
            {
                var answer = await client.ChooseAsync(request, cancellationToken).ConfigureAwait(false);
                raw = answer.RawResponse;
                if (!candidates.ContainsKey(answer.Choice)) throw new JsonException("Unknown action ID.");
                selected = answer.Choice;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Conservative fallback: end this turn, preserve all existing commitments.
                raw = (ex as JevResponseException)?.RawResponse ?? raw;
                failure = $"fallback: {ex.GetType().Name}: {ex.Message}";
            }
            var action = candidates[selected];
            var result = new BotCommandExecutor().Execute(action.Command, session, teamId, random);
            var outcome = failure ?? (result switch
            {
                BotCommandExecutionResult.Success => "success",
                BotCommandExecutionResult.Nop => "finish",
                BotCommandExecutionResult.DomainError error => $"domain-error: {error.Message}",
                _ => throw new InvalidOperationException("Unexpected command result."),
            });
            await writeLog(new(session.State.CurrentTurn, step + 1, request, raw, selected, action.Command, outcome, watch.Elapsed.TotalMilliseconds)).ConfigureAwait(false);
            if (result is BotCommandExecutionResult.Nop or BotCommandExecutionResult.DomainError)
                return failure is not null || result is BotCommandExecutionResult.DomainError;
            used.Add(Slot(action));
        }
        return false;
    }

    // One decision per control per turn: prevents hire/fire and recipe/sale oscillation.
    private static string Slot(JevAction action) => action.Command.Kind switch
    {
        BotCommandKind.SetWorkerCount => $"workers:{action.Command.FactoryId}",
        BotCommandKind.SelectRecipe => $"recipe:{action.Command.FactoryId}",
        BotCommandKind.SellToSystem => $"sale:{action.Command.MaterialId}",
        BotCommandKind.SetGenerationResearchCommitment => "research",
        _ => action.Id,
    };

    private static object AssistedState(GameSession session, JevAnalysis analysis) => new
    {
        game = "Production-chain business simulation. Production runs lower to higher levels, then system sales settle. " +
            "A new factory starts without workers. Hiring ramps over turns. Negative balance is allowed but recurring costs continue. " +
            "Research unlocks higher generation factories. System sales last one tick; research and staffing commitments persist. " +
            "Goal: a profitable complete production chain, not just many factories. Exact end turn is hidden.",
        handbook = new[]
        {
            "Research is the prerequisite for locked higher processing. Check investment needed and time to unlock against the remaining horizon.",
            "A new factory needs workers and input supply; do not count nominal full-supply profit as guaranteed cash.",
            "Sell surplus, retaining inputs for own processing. Unsold inventory is not cash and can incur storage fees.",
            "Falling condition reduces output and raises upkeep. Compare timely overhaul cost with forced downtime.",
            "Persistent research is a recurring expense: reduce or cancel it when the target is unlocked or no longer justified.",
            "Cash trend includes investment. A temporary negative balance alone does not prove a productive investment is bad.",
        },
        analysis,
        warehouse = session.State.Config.Raw.Warehouse,
    };

    private static object BuildState(GameSession session, Ulid teamId)
    {
        var state = session.State;
        var team = state.Teams[teamId];
        var config = state.Config.Raw;
        return new
        {
            game = "Offline production-chain business simulation. Build raw extraction then deeper processing. " +
                "Each tick hires workers, produces from lower to higher levels, then settles sales and recurring costs. " +
                "Negative balance is allowed but costly. Aim for profitable development through the final product, not idle raw stock. " +
                "No automatic workers on a new factory. Market sale requests last one tick; research/worker commitments persist. " +
                "One change per control per turn; actions budget is limited. Nominal output assumes sufficient inputs.",
            turn = state.CurrentTurn,
            duration = new { config.Duration.MinTurns, config.Duration.MaxTurns },
            balance = decimal.Round(team.Balance, 2),
            generation = team.UnlockedGeneration,
            research = new { invested = team.GenerationResearchInvestment, recurring = team.GenerationResearchCommitmentPerTurn,
                config.GenerationResearch.ResearchPointThresholdsByGeneration, config.GenerationResearch.DiminishingReturnsExponent },
            config.WorkerProductivity,
            config.Wear,
            warehouse = new { config.Warehouse.FreeCapacity, config.Warehouse.OverageFeePerUnit },
            electricity = new { state.Market.ElectricityPrice, config.Economy.ElectricityConsumptionPerOutputUnit },
            stock = team.Warehouse.Stock.OrderBy(s => s.Material.Id, StringComparer.Ordinal).Select(s => new { material = s.Material.Id, quantity = decimal.Round(s.Quantity, 2) }).ToArray(),
            factories = team.Factories.OrderBy(f => f.Id).Select(f => new { id = f.Id.ToString(), type = f.Definition.Id, recipe = f.SelectedRecipe.Id,
                workers = f.Workers, desiredWorkers = f.DesiredWorkers, condition = decimal.Round(f.Condition, 3), f.IsUnderRepair, f.OverhaulRequested }).ToArray(),
            pendingSales = team.PendingSaleVolumeByMaterial.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
            // Includes locked recipes so the model can plan research and complete chains.
            catalogue = state.Config.FactoryDefinitions.Where(d => d.Sector == team.Sector).OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => new
            {
                type = d.Id, generationRequired = d.Recipes[0].Output.Level,
                costs = config.FactoryDefinitions.Single(c => c.Id == d.Id),
                recipes = d.Recipes.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new { r.Id, output = r.Output.Id, r.OutputQuantity, r.ProductionRate,
                    inputs = r.Inputs.Select(i => new { material = i.Material.Id, i.Quantity }).ToArray() }).ToArray(),
            }).ToArray(),
            market = state.Config.Materials.Values.Where(m => state.Market.HasQuote(m.Id)).OrderBy(m => m.Id, StringComparer.Ordinal)
                .Select(m => new { material = m.Id, quote = state.Market.QuoteOf(m.Id) }).ToArray(),
        };
    }
}
