using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Вложение в исследование следующего поколения фабрик плюс, если накопленное вложение перешагнуло
/// один или несколько порогов, столько же событий перехода поколения подряд (тот же приём, что
/// <see cref="RndInvestmentStep"/> для одной фабрики, только на уровне команды). Возвращает готовые
/// события, не применяет их — тот же принцип, что у <see cref="ProductionCalculator"/> и
/// <see cref="TickFinanceStep"/>.
/// </summary>
public static class GenerationResearchStep
{
    public static IReadOnlyList<Change<GameSessionState>> Run(Ulid teamId, Team team, decimal amount, GenerationResearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(config);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Investment amount must be positive.");
        }

        var changes = new List<Change<GameSessionState>>
        {
            new GenerationResearchInvested { Id = Ulid.NewUlid(), TeamId = teamId, Amount = amount },
        };

        var cumulativeInvestment = team.GenerationResearchInvestment + amount;
        var resultingGeneration = GenerationResearchCalculator.CalculateResultingGeneration(team.UnlockedGeneration, cumulativeInvestment, config);
        for (var generation = team.UnlockedGeneration + 1; generation <= resultingGeneration; generation++)
        {
            changes.Add(new TeamGenerationAdvanced { Id = Ulid.NewUlid(), TeamId = teamId, NewGeneration = generation });
        }

        return changes;
    }
}
