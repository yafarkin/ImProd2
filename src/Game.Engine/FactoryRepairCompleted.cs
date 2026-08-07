namespace Game.Engine;

/// <summary>
/// Простой фабрики закончился (SPEC §5.6, вынужденный или по заказанному капремонту) — состояние
/// восстановлено до того, что было зафиксировано при начале именно этого простоя (<see
/// cref="Domain.Factory.RepairTargetCondition"/>: 1.0 у любой ступени капремонта, меньше — у
/// вынужденного, штраф за то, что до простоя дело довели); счётчик возраста износа сбрасывается,
/// скорость декея снова стартует с базовой (см. <see cref="WearCalculator"/>). Несёт ход завершения
/// (<see cref="Turn"/>) явно, а не читает его из ambient-состояния при повторном применении — тот же
/// приём, что и у <see cref="FactoryBuilt"/>.
/// </summary>
public sealed record FactoryRepairCompleted : Change<GameSessionState>
{
    /// <summary>Команда, которой принадлежит фабрика.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, вышедшая из простоя.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Состояние, до которого восстановлена фабрика — для аудита (совпадает с <see cref="Domain.Factory.RepairTargetCondition"/> на момент до применения).</summary>
    public required decimal NewCondition { get; init; }

    /// <summary>Ход, на котором простой завершился — становится новой точкой отсчёта возраста износа.</summary>
    public required int Turn { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.CompleteRepair(Turn);
    }
}
