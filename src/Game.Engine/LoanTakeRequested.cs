namespace Game.Engine;

/// <summary>
/// Команда объявила желаемую сумму займа на ближайший расчёт (SPEC §4, §5.9: решения не
/// применяются сразу) — само объявление бесплатно и мгновенно видимое в UI, тем же приёмом, что и
/// <see cref="WorkerCountSet"/>: реальное зачисление денег и рост долга (<see cref="LoanTaken"/>)
/// происходят один раз, на расчёте (<see cref="VoluntaryLoanStep"/>). Последнее объявление в
/// пределах хода замещает предыдущее — сколько раз команда ни передумала бы, значение одно.
/// <see cref="Amount"/> = 0 — заявка снята.
/// </summary>
public sealed record LoanTakeRequested : Change<GameSessionState>
{
    /// <summary>Команда, объявившая заявку.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Желаемая сумма займа на ближайший расчёт; 0 — заявка снята.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Teams[TeamId].RequestLoan(Amount);
    }
}
