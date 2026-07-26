using Game.Config.Contracts;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Публичная репутация команды (Блок 6.2, SPEC §7): доля исполненных поставок с затуханием по
/// свежести (период полураспада — из конфига), без «пристрелочных» срывов первых ходов сессии.
/// Считается по отдельным фактам (поставка/срыв/расторжение), не по контрактам — один recurring
/// контракт с десятью поставками и одним срывом даёт одиннадцать независимых, по-разному
/// затухающих замеров, а не единую усреднённую оценку. Читает готовый журнал напрямую (чистая
/// функция от истории, без собственного мутируемого состояния) — тот же принцип, что и у
/// <see cref="MarketCalculator"/>: пересчитывается по требованию, а не хранится и не обновляется
/// по частям.
/// </summary>
public static class ReputationCalculator
{
    public static ReputationResult Calculate(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries,
        IReadOnlyDictionary<Ulid, Contract> contracts,
        Ulid teamId,
        int currentTurn,
        ReputationConfig config)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(config);

        var weightedSuccess = 0m;
        var weightedTotal = 0m;
        var sampleCount = 0;

        foreach (var entry in entries)
        {
            switch (entry.Change)
            {
                case ContractDelivered delivered when contracts[delivered.ContractId].SellerTeamId == teamId:
                    Accumulate(success: true, delivered.Turn, severity: 1m);
                    break;

                case DeliveryMissed missed when contracts[missed.ContractId].SellerTeamId == teamId:
                    if (missed.Turn > config.WarmupTurns)
                    {
                        Accumulate(success: false, missed.Turn, severity: 1m);
                    }
                    break;

                case ContractTerminated { Reason: ContractTerminationReason.Voluntary } terminated
                    when terminated.TerminatingTeamId == teamId:
                    if (terminated.Turn > config.WarmupTurns)
                    {
                        Accumulate(success: false, terminated.Turn, severity: config.TerminationSeverityMultiplier);
                    }
                    break;
            }
        }

        var percentage = weightedTotal > 0 ? weightedSuccess / weightedTotal * 100m : 100m;
        return new ReputationResult { Percentage = percentage, SampleCount = sampleCount };

        void Accumulate(bool success, int eventTurn, decimal severity)
        {
            var age = currentTurn - eventTurn;
            var decay = (decimal)Math.Pow(0.5, (double)age / config.HalfLifeTurns);
            var weight = severity * decay;

            weightedTotal += weight;
            if (success)
            {
                weightedSuccess += weight;
            }
            sampleCount++;
        }
    }
}
