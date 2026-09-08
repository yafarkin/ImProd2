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

    /// <summary>
    /// Сколько рабочих одна фабрика успевает нанять за один ход (docs/TODO.md №25). Объявить можно
    /// сколько угодно — <see cref="Game.Domain.Factory.DesiredWorkers"/> не ограничен; растянут не
    /// замысел, а его исполнение: разница закрывается по <c>MaxHiresPerTurn</c> человек за ход, пока
    /// не сойдётся. Увольнение под этот предел не подпадает — оно мгновенное (и потому дорогое, см.
    /// <see cref="FireCostPerWorker"/>): нанимать людей долго, а расстаться можно в один день.
    ///
    /// <para>
    /// Ноль или отрицательное значение запрещено (иначе фабрику нельзя укомплектовать никогда).
    /// Осмысленный порядок величины — доля <see cref="BaseWorkerCount"/>: при базе 10 и пределе 5
    /// новая фабрика выходит на штат за два хода, а удвоение штата занимает четыре. Исключение —
    /// добыча (рецепт с выходом уровня 0): там наём всегда мгновенный, см.
    /// <c>WorkforceStep.IsInstantHiring</c>.
    /// </para>
    /// </summary>
    public required int MaxHiresPerTurn { get; init; }

    /// <summary>
    /// Разовая плата за увольнение одного рабочего. Намеренно выше <see cref="HireCostPerWorker"/>:
    /// увольнение мгновенно, и его цена — плата за эту мгновенность (docs/TODO.md №25).
    /// </summary>
    public required decimal FireCostPerWorker { get; init; }

    /// <summary>Зарплата одного рабочего за ход — списывается на финансовом шаге каждого тика.</summary>
    public required decimal SalaryPerWorkerPerTurn { get; init; }
}
