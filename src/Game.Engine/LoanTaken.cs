namespace Game.Engine;

/// <summary>
/// Команда взяла заём по собственному решению (стартовый кредит или любой последующий, SPEC §5.1,
/// §5.9) — в отличие от <see cref="ForcedLoanTaken"/>, инициатор здесь команда, а не движок; сумма
/// и цель займа — решение команды, зафиксированное этим событием, не связанное напрямую ни с каким
/// изменением состояния до него.
/// </summary>
public sealed record LoanTaken : Change<GameSessionState>
{
    /// <summary>Команда, взявшая заём.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Сумма займа.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Teams[TeamId].TakeLoan(Amount);
    }
}
