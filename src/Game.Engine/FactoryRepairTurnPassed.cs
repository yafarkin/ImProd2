namespace Game.Engine;

/// <summary>
/// Один ход простоя фабрики прошёл (SPEC §5.6, вынужденного или добровольного — см. <see
/// cref="FactoryEnteredRepair"/>/<see cref="FactoryOverhaulStarted"/>): выпуск снижен по множителю,
/// зарплата и содержание списаны по тарифам, зафиксированным при начале именно этого простоя (<see
/// cref="Domain.Factory.RepairSalaryRate"/>/<see cref="Domain.Factory.RepairUpkeepRate"/>) — не
/// обязательно льготным: у лёгкой ступени капремонта зарплата и содержание могут идти по полной
/// ставке, фабрика ведь фактически работает. Если это был последний ход простоя, вместе с этим
/// событием в журнал добавляется ещё и <see cref="FactoryRepairCompleted"/>.
/// </summary>
public sealed record FactoryRepairTurnPassed : Change<GameSessionState>
{
    /// <summary>Команда, которой принадлежит фабрика.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, находящаяся в простое.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Сколько ходов простоя останется после этого — для аудита.</summary>
    public required int TurnsRemainingAfter { get; init; }

    /// <summary>Зарплата, выплаченная рабочим фабрики по льготному тарифу простоя.</summary>
    public required decimal SalaryPaid { get; init; }

    /// <summary>Содержание фабрики, списанное по льготному тарифу простоя.</summary>
    public required decimal UpkeepPaid { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);

        var total = SalaryPaid + UpkeepPaid;
        if (total > 0)
        {
            team.Debit(total);
        }

        factory.AdvanceRepairTurn();
    }
}
