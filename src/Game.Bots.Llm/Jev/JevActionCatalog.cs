using System.Globalization;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Llm.Jev;

public sealed record JevAction(string Id, string Description, BotCommand Command);

/// <summary>Read-only action generation. Bounds describe this experiment, not engine restrictions.</summary>
public sealed class JevActionCatalog(JevBotOptions options)
{
    public IReadOnlyList<JevAction> Build(GameSession session, Ulid teamId)
    {
        options.Validate();
        if (session.State.IsFinished || session.State.IsPaused || session.State.CurrentPhase != TurnPhase.Decision)
            throw new InvalidOperationException("Actions require an active decision phase.");
        var state = session.State;
        var config = state.Config.Raw;
        var team = state.Teams[teamId];
        var actions = new List<JevAction> { new("finish", "Finish decisions for this turn; keep existing commitments.", new() { Kind = BotCommandKind.Nop }) };
        void Add(string id, string description, BotCommand command) => actions.Add(new(id, description, command));
        static string N(decimal n) => n.ToString("0.##", CultureInfo.InvariantCulture);

        foreach (var definition in state.Config.FactoryDefinitions.Where(d => d.Sector == team.Sector).OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            if (definition.Recipes[0].Output.Level > team.UnlockedGeneration) continue;
            var costs = config.FactoryDefinitions.Single(d => d.Id == definition.Id);
            foreach (var recipe in definition.Recipes.OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                if (team.Factories.Count(f => f.Definition.Id == definition.Id && f.SelectedRecipe.Id == recipe.Id) >= options.MaxFactoriesPerRecipe) continue;
                var inputs = recipe.Inputs.Count == 0 ? "none (raw extraction)" : string.Join(", ", recipe.Inputs.Select(i => $"{N(i.Quantity)} {i.Material.Id}"));
                Add($"build:{definition.Id}:{recipe.Id}",
                    $"Build {definition.Id}, recipe {recipe.Id}: inputs {inputs} per {N(recipe.OutputQuantity)} {recipe.Output.Id}. " +
                    $"Immediate cost {N(costs.BuildCost)}, fixed cost/turn {N(costs.FixedCostPerTurn)}; starts with ZERO workers; hire separately. Existing recipe factories {team.Factories.Count(f => f.SelectedRecipe.Id == recipe.Id)}.",
                    new() { Kind = BotCommandKind.BuildFactory, FactoryDefinitionId = definition.Id, RecipeId = recipe.Id });
            }
        }
        foreach (var factory in team.Factories.OrderBy(f => f.Id))
        {
            foreach (var workers in options.WorkerMultipliers.Select(m => (int)(config.WorkerProductivity.BaseWorkerCount * m)).Distinct().Order())
            {
                if (workers == factory.DesiredWorkers) continue;
                var output = ProductionCalculator.CalculateCapacityBreakdown(factory, workers, config.WorkerProductivity, config.Rnd).TheoreticalMaxOutput;
                Add($"workers:{factory.Id}:{workers}", $"Set {factory.Definition.Id}/{factory.Id} desired workers to {workers}; current desired {factory.DesiredWorkers}. " +
                    $"Salary/turn {N(workers * config.WorkerProductivity.SalaryPerWorkerPerTurn)}; nominal output {N(output)} {factory.SelectedRecipe.Output.Id}/turn IF inputs available; hiring ramp applies.",
                    new() { Kind = BotCommandKind.SetWorkerCount, FactoryId = factory.Id, Count = workers });
            }
            foreach (var recipe in factory.Definition.Recipes.Where(r => r != factory.SelectedRecipe).OrderBy(r => r.Id, StringComparer.Ordinal))
                Add($"recipe:{factory.Id}:{recipe.Id}", $"Switch {factory.Definition.Id}/{factory.Id} to {recipe.Id}.", new() { Kind = BotCommandKind.SelectRecipe, FactoryId = factory.Id, RecipeId = recipe.Id });
            if (!factory.IsUnderRepair && factory.Condition < 1 && !factory.OverhaulRequested)
                Add($"repair:{factory.Id}", $"Request overhaul of {factory.Definition.Id}/{factory.Id}, condition {N(factory.Condition)}; repair costs and downtime apply.", new() { Kind = BotCommandKind.SetOverhaulRequested, FactoryId = factory.Id, Enabled = true });
        }
        foreach (var material in state.Config.Materials.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            if (!state.Market.HasQuote(material.Id)) continue;
            var stock = team.Warehouse.QuantityOf(material);
            var reserve = team.Factories.Sum(f => f.SelectedRecipe.Inputs.Where(i => i.Material == material).Sum(i =>
                i.Quantity / f.SelectedRecipe.OutputQuantity * ProductionCalculator.CalculateCapacityBreakdown(f, f.DesiredWorkers, config.WorkerProductivity, config.Rnd).TheoreticalMaxOutput)) * options.InputReserveTurns;
            foreach (var volume in new[] { Math.Max(0, stock - reserve), stock }.Distinct().Order())
            {
                if (volume <= 0 || team.PendingSaleVolumeByMaterial.GetValueOrDefault(material.Id) == volume) continue;
                var quote = state.Market.QuoteOf(material.Id);
                Add($"sell:{material.Id}:{volume.ToString(CultureInfo.InvariantCulture)}", $"Request sale of {N(volume)} {material.Id} from stock {N(stock)}; input reserve estimate {N(reserve)}; current price {N(quote.Price)}, market capacity {N(quote.Capacity)}. Settles next tick AFTER production; price/capacity can change.",
                    new() { Kind = BotCommandKind.SellToSystem, MaterialId = material.Id, Volume = volume });
            }
            if (team.PendingSaleVolumeByMaterial.ContainsKey(material.Id))
                Add($"cancel-sale:{material.Id}", $"Cancel pending system sale of {material.Id}; retain for production.", new() { Kind = BotCommandKind.SellToSystem, MaterialId = material.Id, Volume = 0 });
        }
        foreach (var amount in options.ResearchFractions.Select(f => f * config.GenerationResearch.MaxCommitmentPerTurn).Distinct().Order())
        {
            if (amount == team.GenerationResearchCommitmentPerTurn || (amount > 0 && GenerationResearchCalculator.IsAtMaxGeneration(team.UnlockedGeneration, config.GenerationResearch))) continue;
            Add($"research:{N(amount)}", $"Set recurring generation research to {N(amount)}/turn (until changed); unlock deeper production. Current generation {team.UnlockedGeneration}, invested {N(team.GenerationResearchInvestment)}.", new() { Kind = BotCommandKind.SetGenerationResearchCommitment, Amount = amount });
        }
        return actions;
    }
}
