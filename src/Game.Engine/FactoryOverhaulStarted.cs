namespace Game.Engine;

/// <summary>
/// Заказанный командой капремонт запущен (SPEC §5.6) — тяжесть (цена, простой) определена по
/// состоянию фабрики на момент расчёта (<see cref="ConditionAtStart"/>), не по тому, что фабрика
/// изнашивалась быстро или медленно: <see cref="TierId"/> — какая ступень сработала (см. <see
/// cref="Game.Config.Economy.WearConfig.OverhaulTiers"/>). Всегда восстанавливает фабрику до 1.0 по
/// завершении (в отличие от вынужденного простоя, см. <see cref="FactoryEnteredRepair"/>) — решать
/// самому всегда не хуже, чем ждать. Дальнейшие ходы простоя — см. <see
/// cref="FactoryRepairTurnPassed"/>/<see cref="FactoryRepairCompleted"/>.
/// </summary>
public sealed record FactoryOverhaulStarted : Change<GameSessionState>
{
    /// <summary>Команда, заказавшая капремонт.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, отправленная на капремонт.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Код сработавшей ступени (<c>OverhaulTierConfig.Id</c>) — для аудита.</summary>
    public required string TierId { get; init; }

    /// <summary>Отображаемое имя сработавшей ступени — для аудита/истории, не пересчитывается по TierId при показе.</summary>
    public required string TierName { get; init; }

    /// <summary>Состояние фабрики на момент, когда команда решила чинить — определило, какая ступень сработала.</summary>
    public required decimal ConditionAtStart { get; init; }

    /// <summary>Стоимость (доля от <c>FactoryDefinitionConfig.BuildCost</c> на момент решения).</summary>
    public required decimal Cost { get; init; }

    /// <summary>Сколько ходов действует эта ступень.</summary>
    public required int DurationTurns { get; init; }

    /// <summary>Множитель к выпуску на время действия ступени (0 — полная остановка).</summary>
    public required decimal OutputMultiplier { get; init; }

    /// <summary>Доля обычной зарплаты рабочих фабрики на время действия ступени.</summary>
    public required decimal SalaryRate { get; init; }

    /// <summary>Доля обычного содержания фабрики на время действия ступени.</summary>
    public required decimal UpkeepRate { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);

        if (Cost > 0)
        {
            team.Debit(Cost);
        }

        factory.StartRepair(ConditionAtStart, DurationTurns, OutputMultiplier, SalaryRate, UpkeepRate, targetCondition: 1m);
    }
}
