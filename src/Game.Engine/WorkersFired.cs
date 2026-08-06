using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Фабрика реально уволила рабочих — до объявленной командой численности (см. doc-comment <see
/// cref="WorkersHired"/>). Разовая плата, списывается один раз за ход на финансовом шаге тика, а не
/// в момент объявления (см. <see cref="TickFinanceStep"/>, <see cref="WorkforceStep"/>).
/// </summary>
public sealed record WorkersFired : Change<GameSessionState>
{
    /// <summary>Команда, уволившая рабочих.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, с которой уволены рабочие.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Число уволенных рабочих.</summary>
    public required int Count { get; init; }

    /// <summary>Разовая плата за увольнение (Count × FireCostPerWorker на момент действия).</summary>
    public required decimal Cost { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.Fire(Count);
        if (Cost > 0)
        {
            team.Debit(Cost);
        }
    }
}
