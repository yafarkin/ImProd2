using Game.Config.Economy;
using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Приводит фактическую численность рабочих одной фабрики к объявленной (<see
/// cref="Factory.DesiredWorkers"/>) и считает разовую плату за наём/увольнение по итоговой разнице
/// (SPEC §5.6, запрос пользователя: сколько бы раз команда ни меняла число рабочих за ход, списать
/// один раз, а не за каждое промежуточное значение) — тот же приём «объявление + автосписание», что и
/// у <see cref="RndInvestmentStep"/>. Возвращает готовое событие, не применяет его; <see
/// langword="null"/>, если разницы нет.
/// </summary>
public static class WorkforceStep
{
    public static Change<GameSessionState>? Run(Ulid teamId, Factory factory, WorkerProductivityConfig config)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(config);

        var delta = factory.DesiredWorkers - factory.Workers;
        if (delta == 0)
        {
            return null;
        }

        if (delta > 0)
        {
            return new WorkersHired
            {
                Id = Ulid.NewUlid(),
                TeamId = teamId,
                FactoryId = factory.Id,
                Count = delta,
                Cost = delta * config.HireCostPerWorker,
            };
        }

        var fireCount = -delta;
        return new WorkersFired
        {
            Id = Ulid.NewUlid(),
            TeamId = teamId,
            FactoryId = factory.Id,
            Count = fireCount,
            Cost = fireCount * config.FireCostPerWorker,
        };
    }
}
