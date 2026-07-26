namespace Game.Engine;

/// <summary>Выплачена зарплата всем рабочим команды за ход (SPEC §5.6/§5.9), суммарно по всем фабрикам.</summary>
public sealed record SalariesPaid : Change<GameSessionState>
{
    /// <summary>Команда, выплатившая зарплату.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Суммарное число рабочих, за которых выплачена зарплата — для аудита.</summary>
    public required int TotalWorkers { get; init; }

    /// <summary>Выплаченная сумма.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Teams[TeamId].Debit(Amount);
    }
}
