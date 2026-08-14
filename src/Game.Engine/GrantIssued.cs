namespace Game.Engine;

/// <summary>
/// Ведущий выдал безвозмездный грант отстающей команде (Блок 9.6, SPEC §9.5) — в отличие от
/// <see cref="LoanTaken"/>/<see cref="ForcedLoanTaken"/>, сам по себе не увеличивает <c>Debt</c>.
/// </summary>
public sealed record GrantIssued : Change<GameSessionState>
{
    /// <summary>Команда, получившая грант.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Сумма гранта.</summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Если true — грант сначала гасит тело существующего долга (в пределах <see
    /// cref="Team.Debt"/> на момент применения), остаток сверх долга зачисляется на баланс как
    /// обычно. Иначе (по умолчанию) весь грант идёт на баланс, долг не трогает — так ведущий
    /// защищает команду от того, что деньги разойдутся на текущие траты, а не на выход из долгов, и
    /// команда тут же снова попадёт в принудительный заём.
    /// </summary>
    public bool RepayDebtFirst { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        team.Credit(Amount);

        if (RepayDebtFirst && team.Debt > 0)
        {
            var repayment = Math.Min(Amount, team.Debt);
            team.Debit(repayment);
            team.RepayLoan(repayment);
        }
    }
}
