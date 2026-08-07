namespace Game.Config.Economy;

/// <summary>
/// Параметры износа и капремонта фабрик (SPEC §5.6): без вложений фабрика со временем теряет
/// состояние (<c>Factory.Condition</c>), скорость потери сама ускоряется с возрастом — незаметно в
/// первые ходы после постройки, затем всё быстрее. Единственный способ противостоять износу —
/// действие «Капремонт» (<see cref="OverhaulTiers"/>, декларируется в фазу решений, применяется в
/// фазу расчёта, как и всё остальное): тяжесть последствий (цена, простой) зависит от того,
/// насколько фабрика изношена на момент решения, а не от того, разово чинит команда или регулярно —
/// запрос пользователя: это должно быть настоящее действие с операционной ценой, а не просто вычет
/// из баланса. Если команда полностью игнорирует фабрику до <see cref="CriticalConditionThreshold"/>,
/// движок сам принудительно останавливает её (safety net) — хуже, чем любая ступень капремонта,
/// специально чтобы решать самому было выгоднее, чем ждать. Числа — заглушки, требуют калибровки.
/// </summary>
public sealed record WearConfig
{
    /// <summary>
    /// Сколько ходов после постройки (или последнего капремонта/вынужденного простоя, см.
    /// <c>Factory.LastResetTurn</c>) фабрика не изнашивается вовсе — запрос пользователя: не ломать
    /// уже откалиброванное равновесие первых ходов сессии.
    /// </summary>
    public required int GracePeriodTurns { get; init; }

    /// <summary>
    /// Базовая скорость снижения <c>Factory.Condition</c> за ход сразу по истечении льготного периода
    /// (при возрасте сверх льготы = 1). Дальше скорость растёт линейно, см. <see
    /// cref="AccelerationFactorPerTurn"/> — сама механика не константна, а ускоряется с возрастом.
    /// </summary>
    public required decimal BaseWearRatePerTurn { get; init; }

    /// <summary>
    /// Прирост скорости декея за каждый ход сверх льготного периода:
    /// <c>decayRate(t) = BaseWearRatePerTurn + AccelerationFactorPerTurn × t</c>, где t — возраст сверх
    /// льготы. Это то, что делает первые ходы почти незаметными, а дальнейшие — резко тяжелее.
    /// </summary>
    public required decimal AccelerationFactorPerTurn { get; init; }

    /// <summary>
    /// Множитель штрафа к <c>FactoryUpkeepPaid</c> при <c>Condition == CriticalConditionThreshold</c>
    /// (линейная интерполяция от ×1.0 при <c>Condition = 1</c>) — предупреждающее давление на бюджет,
    /// нарастающее ещё до того, как понадобится капремонт.
    /// </summary>
    public required decimal MaxUpkeepPenaltyMultiplier { get; init; }

    /// <summary>
    /// Ступени действия «Капремонт», упорядочены по убыванию <see
    /// cref="OverhaulTierConfig.MinCondition"/> — см. doc-comment <see cref="OverhaulTierConfig"/> и
    /// <see cref="Game.Engine.WearCalculator.SelectTier"/>. Нижняя граница самой слабой ступени не
    /// должна быть ниже <see cref="CriticalConditionThreshold"/>.
    /// </summary>
    public required IReadOnlyList<OverhaulTierConfig> OverhaulTiers { get; init; }

    /// <summary>
    /// Порог <c>Factory.Condition</c> (строго ниже нижней границы самой слабой ступени <see
    /// cref="OverhaulTiers"/>), при пересечении которого без вмешательства команды движок сам
    /// принудительно останавливает фабрику (safety net, запрос пользователя: не мягкий пол-плато, а
    /// настоящее «выбывание из строя») — хуже любой ступени капремонта, см. <see
    /// cref="PostForcedRepairCondition"/>, чтобы решать самому было выгоднее, чем игнорировать.
    /// </summary>
    public required decimal CriticalConditionThreshold { get; init; }

    /// <summary>Сколько ходов длится вынужденный простой после пересечения порога.</summary>
    public required int ForcedRepairDurationTurns { get; init; }

    /// <summary>
    /// Доля обычной зарплаты, которая всё равно платится рабочим фабрики на время вынужденного
    /// простоя (не 0% — это простой, а не увольнение; не 100% — фабрика не производит). Тот же смысл
    /// у тяжёлых ступеней капремонта, см. <see cref="OverhaulTierConfig.SalaryRate"/>.
    /// </summary>
    public required decimal ForcedRepairSalaryRate { get; init; }

    /// <summary>Доля обычного содержания фабрики на время вынужденного простоя — тот же смысл, что <see cref="ForcedRepairSalaryRate"/>, для содержания.</summary>
    public required decimal ForcedRepairUpkeepRate { get; init; }

    /// <summary>
    /// Состояние, до которого фабрика восстанавливается по окончании вынужденного простоя —
    /// намеренно не 1.0, как у любой ступени капремонта, а меньше (штраф за то, что до простоя дело
    /// довели), например 0.85.
    /// </summary>
    public required decimal PostForcedRepairCondition { get; init; }
}
