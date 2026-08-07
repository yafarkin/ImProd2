namespace Game.Config.Economy;

/// <summary>
/// Одна ступень действия «Капремонт» (SPEC §5.6) — тяжесть последствий зависит от того, насколько
/// изношена фабрика в момент, когда команда решает вложиться, а не от того, разово чинит или
/// регулярно (запрос пользователя: капремонт должен быть действием с настоящей операционной ценой —
/// простоем, а не просто вычетом из баланса). Ступени в <see cref="WearConfig.OverhaulTiers"/>
/// упорядочены по убыванию <see cref="MinCondition"/>; действует первая ступень, чьему порогу
/// удовлетворяет текущее состояние фабрики (<see cref="Game.Engine.WearCalculator.SelectTier"/>).
/// </summary>
public sealed record OverhaulTierConfig
{
    /// <summary>Код ступени — для аудита в событиях, не показывается напрямую игроку.</summary>
    public required string Id { get; init; }

    /// <summary>Отображаемое имя ступени (например, «Лёгкое обслуживание», «Капремонт», «Полная реконструкция»).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Нижняя граница состояния фабрики, при котором действует эта ступень (включительно). У самой
    /// нижней ступени не должна быть ниже <see cref="WearConfig.CriticalConditionThreshold"/> —
    /// иначе между «ни одна ступень не подходит» и автоматическим вынужденным простоем образуется
    /// мёртвая зона, где команда видит проблему, но не может на неё среагировать (совпадение границ
    /// — нормально: пока команда решает вынести капремонт в этот же ход, порог ещё не сработал, см.
    /// <see cref="Game.Engine.WearStep"/>).
    /// </summary>
    public required decimal MinCondition { get; init; }

    /// <summary>Стоимость — доля от <c>FactoryDefinitionConfig.BuildCost</c> этой фабрики.</summary>
    public required decimal CostFraction { get; init; }

    /// <summary>Сколько ходов действует эффект этой ступени (простой либо сниженный выпуск, см. <see cref="OutputMultiplier"/>).</summary>
    public required int DurationTurns { get; init; }

    /// <summary>
    /// Множитель к выпуску фабрики на время <see cref="DurationTurns"/>: 0 — полная остановка
    /// (тяжёлые ступени), меньше 1, но больше 0 — частичное снижение при лёгком, частом обслуживании
    /// (запрос пользователя: «если делаем капремонт постоянно, снижение небольшое»).
    /// </summary>
    public required decimal OutputMultiplier { get; init; }

    /// <summary>
    /// Доля обычной зарплаты рабочих фабрики на время действия ступени — 1.0 у лёгких ступеней
    /// (фабрика фактически работает, просто чуть менее производительно, увольнять/недоплачивать
    /// некого), меньше 1.0 у тяжёлых (настоящий простой, тот же приём, что у вынужденного простоя,
    /// см. <see cref="WearConfig.ForcedRepairSalaryRate"/>).
    /// </summary>
    public required decimal SalaryRate { get; init; }

    /// <summary>Доля обычного содержания фабрики (<c>FactoryDefinitionConfig.FixedCostPerTurn</c>) на время действия ступени — тот же смысл, что <see cref="SalaryRate"/>, для содержания.</summary>
    public required decimal UpkeepRate { get; init; }
}
