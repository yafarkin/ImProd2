using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Фабрика реально наняла рабочих — до объявленной командой численности (<see
/// cref="Domain.Factory.DesiredWorkers"/>), см. <see cref="GameSession.SetWorkerCount"/>). Разовая
/// плата, но списывается не в момент объявления, а один раз за ход, на финансовом шаге тика (<see
/// cref="TickFinanceStep"/>, <see cref="WorkforceStep"/>) — тем же приёмом, что и R&amp;D (см. <see
/// cref="RndInvested"/>), в отличие от постройки фабрики (<see cref="FactoryBuilt"/>), которая
/// по-прежнему платится сразу в момент действия.
/// </summary>
public sealed record WorkersHired : Change<GameSessionState>
{
    /// <summary>Команда, нанявшая рабочих.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, на которую наняты рабочие.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Число нанятых рабочих.</summary>
    public required int Count { get; init; }

    /// <summary>Разовая плата за наём (Count × HireCostPerWorker на момент действия).</summary>
    public required decimal Cost { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.Hire(Count);
        if (Cost > 0)
        {
            team.Debit(Cost);
        }
    }
}
