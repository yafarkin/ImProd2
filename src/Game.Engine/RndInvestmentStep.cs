using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Вложение в R&amp;D конкретной фабрики плюс, если накопленное вложение перешагнуло один или
/// несколько порогов, столько же событий перехода уровня подряд (SPEC §5.8). Возвращает готовые
/// события, не применяет их — тот же принцип, что у <see cref="ProductionCalculator"/> и
/// <see cref="TickFinanceStep"/>.
/// </summary>
public static class RndInvestmentStep
{
    public static IReadOnlyList<Change<GameSessionState>> Run(Ulid teamId, Factory factory, decimal amount, RndConfig config)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(config);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Investment amount must be positive.");
        }

        var changes = new List<Change<GameSessionState>>
        {
            new RndInvested { Id = Ulid.NewUlid(), TeamId = teamId, FactoryId = factory.Id, Amount = amount },
        };

        var cumulativeInvestment = factory.RndInvestment + amount;
        var resultingLevel = RndCalculator.CalculateResultingLevel(factory.Level, cumulativeInvestment, config);
        for (var level = factory.Level + 1; level <= resultingLevel; level++)
        {
            changes.Add(new FactoryLevelAdvanced { Id = Ulid.NewUlid(), TeamId = teamId, FactoryId = factory.Id, NewLevel = level });
        }

        return changes;
    }
}
