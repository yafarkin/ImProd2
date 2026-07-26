using Game.Domain;

namespace Game.Engine;

/// <summary>Команда уволила рабочих с фабрики (SPEC §5.6: увольнение мгновенное, с разовой платой за действие).</summary>
public sealed record WorkersFired : Change<Team>
{
    /// <summary>Фабрика, с которой уволены рабочие.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Число уволенных рабочих.</summary>
    public required int Count { get; init; }

    /// <summary>Разовая плата за увольнение (Count × FireCostPerWorker на момент действия).</summary>
    public required decimal Cost { get; init; }

    public override void Apply(Team state)
    {
        var factory = state.Factories.Single(f => f.Id == FactoryId);
        factory.Fire(Count);
        if (Cost > 0)
        {
            state.Debit(Cost);
        }
    }
}
