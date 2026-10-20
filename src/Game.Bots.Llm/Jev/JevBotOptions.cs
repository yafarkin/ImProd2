namespace Game.Bots.Llm.Jev;

/// <summary>Experimental policy limits, separate from game rules. Requires calibration.</summary>
public sealed record JevBotOptions
{
    public int MaxActionsPerTurn { get; init; } = 12;
    public int MaxFactoriesPerRecipe { get; init; } = 2;
    public decimal InputReserveTurns { get; init; } = 1;
    public decimal[] WorkerMultipliers { get; init; } = [0, 1, 2];
    public decimal[] ResearchFractions { get; init; } = [0, 0.25m, 1];

    public void Validate()
    {
        if (MaxActionsPerTurn < 1 || MaxFactoriesPerRecipe < 1 || InputReserveTurns < 0
            || WorkerMultipliers.Length == 0 || WorkerMultipliers.Any(x => x < 0 || x > 100)
            || ResearchFractions.Length == 0 || ResearchFractions.Any(x => x < 0 || x > 1))
            throw new ArgumentException("Invalid JEV experimental policy options.");
    }
}
