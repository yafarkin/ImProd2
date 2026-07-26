namespace Game.Config.Contracts;

/// <summary>
/// Параметры репутации (SPEC §7): затухание свежих срывов, «пристрелочные» ходы в начале сессии,
/// когда срывы не бьют по публичной репутации. Числа — заглушки, требуют калибровки.
/// </summary>
public sealed record ReputationConfig
{
    /// <summary>Период полураспада веса срыва поставки, в ходах.</summary>
    public required int HalfLifeTurns { get; init; }

    /// <summary>Число «пристрелочных» ходов в начале сессии, когда срывы не идут в публичную репутацию.</summary>
    public required int WarmupTurns { get; init; }

    /// <summary>
    /// Во сколько раз вес одностороннего (voluntary) расторжения контракта тяжелее веса обычного
    /// срыва поставки того же хода (SPEC §6: «существенно выше... репутационный удар»). Обоюдное
    /// (mutual) расторжение на репутацию не влияет вовсе — это не несоблюдение.
    /// </summary>
    public required decimal TerminationSeverityMultiplier { get; init; }
}
