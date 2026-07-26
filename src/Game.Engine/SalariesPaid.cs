using Game.Domain;

namespace Game.Engine;

/// <summary>Выплачена зарплата всем рабочим команды за ход (SPEC §5.6/§5.9), суммарно по всем фабрикам.</summary>
public sealed record SalariesPaid : Change<Team>
{
    /// <summary>Суммарное число рабочих, за которых выплачена зарплата — для аудита.</summary>
    public required int TotalWorkers { get; init; }

    /// <summary>Выплаченная сумма.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(Team state)
    {
        state.Debit(Amount);
    }
}
