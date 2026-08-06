using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Команда объявила желаемую численность рабочих фабрики на ближайший расчёт (SPEC §5.6, запрос
/// пользователя: сколько бы раз команда ни передумала за ход, списать деньги только один раз, по
/// итоговой разнице) — само объявление бесплатно и мгновенно, тем же приёмом, что и <see
/// cref="RndCommitmentSet"/>; реальный наём/увольнение и разовая плата за него происходят отдельно,
/// один раз за ход, на фазе расчёта (см. <see cref="TickFinanceStep"/>, <see cref="WorkforceStep"/>).
/// </summary>
public sealed record WorkerCountSet : Change<GameSessionState>
{
    /// <summary>Команда, которой принадлежит фабрика.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, для которой меняют желаемую численность.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Новая желаемая численность рабочих.</summary>
    public required int Count { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.SetDesiredWorkers(Count);
    }
}
