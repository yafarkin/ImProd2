namespace Game.Engine;

/// <summary>
/// Результат расчёта производства одной фабрики за один тик (Блок 4.2, SPEC §5.6). Отделён от
/// самого события <see cref="FactoryProduced"/>, потому что расчёт — чистая функция от текущего
/// состояния (<see cref="ProductionCalculator.Calculate"/>), а решение о том, с каким
/// <c>Ulid</c> и когда это применить, остаётся за вызывающим кодом (тот же принцип, что и у
/// <see cref="SessionEndTurnDraw"/>).
/// </summary>
public sealed record ProductionResult
{
    /// <summary>Фабрика, для которой выполнен расчёт.</summary>
    public required Ulid FactoryId { get; init; }

    /// <summary>
    /// Сколько удалось бы произвести исходя только из мощности (рабочие), без учёта наличия сырья —
    /// показывает, ограничено ли фактическое производство нехваткой входов или это и есть предел
    /// мощности.
    /// </summary>
    public required decimal CapacityLimitedOutputQuantity { get; init; }

    /// <summary>Сколько произведено фактически — после ограничения по наличию сырья на складе.</summary>
    public required decimal OutputQuantity { get; init; }

    /// <summary>Фактически списанное количество каждого входного материала (код материала → количество).</summary>
    public required IReadOnlyDictionary<string, decimal> ConsumedInputs { get; init; }
}
