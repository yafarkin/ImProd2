using Game.Config.Session;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Принудительный заём (SPEC §5.9), если баланс команды всё ещё отрицательный. Намеренно отдельный,
/// самый последний шаг расчёта тика — вызывается <see cref="GameSession.RunTick"/> уже после
/// <see cref="TickFinanceStep"/>, производства (<see cref="FactoryProduced.OverheadCost"/>) и
/// исполнения контрактов, а не как часть <see cref="TickFinanceStep"/> (баг-репорт пользователя:
/// раньше решение принималось до этих трёх — команда могла закрыть дыру займом и тут же снова уйти в
/// минус от списания за работу фабрики или проигранного контракта, которые на тот момент ещё не были
/// посчитаны, и это уже никак не покрывалось до следующего хода). Возвращает готовое событие, не
/// применяет его; <see langword="null"/>, если долга нет.
/// </summary>
public static class ForcedLoanStep
{
    public static Change<GameSessionState>? Run(Team team, StartingConditionsConfig loanConfig)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(loanConfig);

        if (team.Balance >= 0m)
        {
            return null;
        }

        return new ForcedLoanTaken
        {
            Id = Ulid.NewUlid(),
            TeamId = team.Id,
            Amount = -team.Balance,
            NewPenaltyRateSurcharge = team.PenaltyRateSurcharge + loanConfig.ForcedLoanPenaltyRatePerOccurrence,
        };
    }
}
