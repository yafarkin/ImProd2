using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Команда наняла рабочих на фабрику (SPEC §5.6: наём мгновенный, с разовой платой за действие).
/// Списание происходит сразу в момент действия, а не на финансовом шаге тика — в отличие от
/// зарплаты, процентов и принудительного кредита (см. <see cref="LoanInterestCharged"/>,
/// <see cref="SalariesPaid"/>), которые накапливаются и применяются раз в ход.
/// </summary>
public sealed record WorkersHired : Change<Team>
{
    /// <summary>Фабрика, на которую наняты рабочие.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Число нанятых рабочих.</summary>
    public required int Count { get; init; }

    /// <summary>Разовая плата за наём (Count × HireCostPerWorker на момент действия).</summary>
    public required decimal Cost { get; init; }

    public override void Apply(Team state)
    {
        var factory = state.Factories.Single(f => f.Id == FactoryId);
        factory.Hire(Count);
        if (Cost > 0)
        {
            state.Debit(Cost);
        }
    }
}
