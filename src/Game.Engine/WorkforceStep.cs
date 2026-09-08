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
///
/// <para>
/// <b>Наём инертен, увольнение — нет (docs/TODO.md №25).</b> За один ход фабрика нанимает не больше
/// <see cref="WorkerProductivityConfig.MaxHiresPerTurn"/> человек; остаток объявленного расхождения
/// закрывается следующими ходами сам, без повторного объявления — <see cref="Factory.DesiredWorkers"/>
/// хранит замысел, а не остаток. Увольнение исполняется целиком в тот же ход, потому и стоит дороже:
/// смысл асимметрии в том, что решение «нарастить» приходится принимать заранее, а решение
/// «сократить» действует немедленно, но бьёт по кассе.
/// </para>
/// </summary>
public static class WorkforceStep
{
    /// <summary>
    /// Добыча нанимает мгновенно, в обход предела: уровень 0 — неквалифицированный труд, который
    /// выходит на смену сразу, в отличие от ролей выше по цепочке (docs/TODO.md №25). Смотрим на
    /// уровень выхода рецепта, а не на <see cref="Factory.Level"/> — последний про R&amp;D-прокачку,
    /// это другая ось.
    /// </summary>
    public static bool IsInstantHiring(Factory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return factory.SelectedRecipe.Output.Level == 0;
    }

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
            var hireCount = IsInstantHiring(factory) ? delta : Math.Min(delta, config.MaxHiresPerTurn);

            return new WorkersHired
            {
                Id = Ulid.NewUlid(),
                TeamId = teamId,
                FactoryId = factory.Id,
                Count = hireCount,
                Cost = hireCount * config.HireCostPerWorker,
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
