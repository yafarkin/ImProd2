using Game.Config.Economy;

namespace Game.Engine;

/// <summary>
/// «Давление» недавних экстренных закупок команды по конкретному материалу (Блок 9.2,
/// пользовательский запрос: наказывать зависимость от рынка, а не саму операцию) — доля от прошлых
/// закупок, ещё не успевшая затухнуть по свежести, тем же приёмом полураспада, что и
/// <see cref="ReputationCalculator"/>. Читает готовый журнал напрямую (чистая функция от истории,
/// без собственного мутируемого состояния) — то же самое, зачем репутация не хранится отдельным
/// полем на <see cref="Game.Domain.Team"/>.
/// </summary>
public static class EmergencyPurchasePressureCalculator
{
    public static decimal CalculateRecentVolume(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries,
        Ulid teamId,
        string materialId,
        int currentTurn,
        EconomyConfig config)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(materialId);
        ArgumentNullException.ThrowIfNull(config);

        var weightedVolume = 0m;

        foreach (var entry in entries)
        {
            if (entry.Change is EmergencyPurchased purchased
                && purchased.TeamId == teamId
                && purchased.MaterialId == materialId)
            {
                var age = currentTurn - purchased.Turn;
                var decay = (decimal)Math.Pow(0.5, (double)age / config.EmergencyPurchasePressureHalfLifeTurns);
                weightedVolume += purchased.Volume * decay;
            }
        }

        return weightedVolume;
    }
}
