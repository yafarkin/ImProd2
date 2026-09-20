using Game.Config.Loading;

namespace Game.Engine;

/// <summary>
/// Ретроспективная сводка по ходам для дебрифа (Блок 10.1, SPEC §12) — реплеит уже записанный
/// журнал на копии состояния и накапливает денежную массу и объём продаж системе по ходам; в
/// <see cref="GameSessionState"/> хранится только текущее значение, история нигде не сохраняется
/// отдельно. Тот же приём, что и у <see cref="ReputationCalculator"/> — принимает
/// <c>Entries</c> и нужный кусок конфига напрямую, а не всю <see cref="GameSession"/>, для
/// тестируемости без полноценной сессии. Переход хода отслеживается по изменению
/// <see cref="GameSessionState.CurrentTurn"/> после каждого <see cref="Change{TState}.Apply"/>, а
/// не по конкретному типу события — устойчиво к любому будущему способу продвинуть ход.
/// </summary>
public static class TurnHistoryCalculator
{
    /// <summary>Сводка одного хода: денежная масса и активность на конец хода (или текущий момент — для ещё не завершённого).</summary>
    public sealed record TurnSummary(int Turn, decimal TotalCash, decimal VolumeSoldToSystem);

    /// <summary>Можно звать в любой момент сессии — последний, возможно неполный ход тоже попадает в сводку.</summary>
    public static IReadOnlyList<TurnSummary> Summarize(IReadOnlyList<EventLogEntry<GameSessionState>> entries, ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(config);

        var scratch = new GameSessionState(config);
        var summaries = new List<TurnSummary>();
        var turn = 0;
        var volumeSold = 0m;

        foreach (var entry in entries)
        {
            entry.Change.Apply(scratch);

            if (entry.Change is MaterialSoldToSystem sale)
            {
                volumeSold += sale.Volume;
            }

            if (scratch.CurrentTurn != turn)
            {
                if (turn > 0)
                {
                    summaries.Add(new TurnSummary(turn, TotalCash(scratch), volumeSold));
                }

                turn = scratch.CurrentTurn;
                volumeSold = 0m;
            }
        }

        if (turn > 0)
        {
            summaries.Add(new TurnSummary(turn, TotalCash(scratch), volumeSold));
        }

        return summaries;
    }

    private static decimal TotalCash(GameSessionState state) => state.Teams.Values.Sum(t => t.Balance);
}
