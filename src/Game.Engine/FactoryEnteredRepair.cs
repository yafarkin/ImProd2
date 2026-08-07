namespace Game.Engine;

/// <summary>
/// Фабрика пересекла критическое состояние (<c>WearConfig.CriticalConditionThreshold</c>) без
/// вмешательства команды и уходит в вынужденный простой (SPEC §5.6, запрос пользователя: не мягкий
/// пол-плато, а настоящее «выбывание из строя» — safety net на случай полного игнора, хуже любой
/// ступени добровольного капремонта, см. <see cref="FactoryOverhaulStarted"/>, специально чтобы
/// решать самому было выгоднее) — решение движка, а не команды, но тоже факт, достойный своей записи
/// (AGENTS-память о трассируемости причин: решения — тоже события). Дальнейшие ходы простоя — см.
/// <see cref="FactoryRepairTurnPassed"/>/<see cref="FactoryRepairCompleted"/>.
/// </summary>
public sealed record FactoryEnteredRepair : Change<GameSessionState>
{
    /// <summary>Команда, которой принадлежит фабрика.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Фабрика, ушедшая в простой.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>Состояние на момент пересечения порога.</summary>
    public required decimal ConditionAtEntry { get; init; }

    /// <summary>Сколько ходов продлится вынужденный простой (<c>WearConfig.ForcedRepairDurationTurns</c> на момент решения).</summary>
    public required int DurationTurns { get; init; }

    /// <summary>Доля обычной зарплаты рабочих фабрики на время простоя (<c>WearConfig.ForcedRepairSalaryRate</c> на момент решения).</summary>
    public required decimal SalaryRate { get; init; }

    /// <summary>Доля обычного содержания фабрики на время простоя (<c>WearConfig.ForcedRepairUpkeepRate</c> на момент решения).</summary>
    public required decimal UpkeepRate { get; init; }

    /// <summary>Состояние, до которого фабрика восстановится по окончании простоя (<c>WearConfig.PostForcedRepairCondition</c> на момент решения) — намеренно не 1.0, штраф за то, что до простоя дело довели.</summary>
    public required decimal TargetCondition { get; init; }

    public override void Apply(GameSessionState state)
    {
        var team = state.Teams[TeamId];
        var factory = team.Factories.Single(f => f.Id == FactoryId);
        factory.StartRepair(ConditionAtEntry, DurationTurns, outputMultiplier: 0m, SalaryRate, UpkeepRate, TargetCondition);
    }
}
