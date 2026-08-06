namespace Game.Config.Economy;

/// <summary>
/// Кривая производительности фабрики от числа рабочих (SPEC §5.6): линейная до базовой
/// численности, с убывающей отдачей сверх неё. Плата за наём/увольнение — разовая, за действие.
/// Все числа — заглушки, требуют калибровки.
/// </summary>
public sealed record WorkerProductivityConfig
{
    /// <summary>Численность рабочих, до которой отдача от найма линейна.</summary>
    public required int BaseWorkerCount { get; init; }

    /// <summary>Множитель отдачи для рабочих сверх базовой численности (убывающая отдача, 0..1).</summary>
    public required decimal DiminishingReturnsFactor { get; init; }

    /// <summary>Разовая плата за найм одного рабочего.</summary>
    public required decimal HireCostPerWorker { get; init; }

    /// <summary>Разовая плата за увольнение одного рабочего.</summary>
    public required decimal FireCostPerWorker { get; init; }

    /// <summary>Зарплата одного рабочего за ход — списывается на финансовом шаге каждого тика.</summary>
    public required decimal SalaryPerWorkerPerTurn { get; init; }

    /// <summary>
    /// Порог по ОБЩЕЙ численности рабочих команды (сумма по всем её фабрикам, не одной фабрики),
    /// после которого зарплата начинает расти быстрее — зеркало <see cref="BaseWorkerCount"/>, но
    /// для стоимости, а не выработки: раздувать одну фабрику становится дороже само по себе, без
    /// штрафов за неудачу (запрос пользователя, спираль между областями).
    /// </summary>
    public required int TeamSalaryBaseWorkerCount { get; init; }

    /// <summary>
    /// Множитель ставки зарплаты для рабочих сверх <see cref="TeamSalaryBaseWorkerCount"/> (&gt; 1 —
    /// дороже базовой ставки). Заглушка, требует калибровки.
    /// </summary>
    public required decimal SalaryEscalationFactor { get; init; }
}
