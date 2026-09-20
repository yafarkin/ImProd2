namespace Game.Engine;

/// <summary>
/// Траектория X(t) одной ветки специализации (Блок 7.3.4, <c>docs/production-balance.md</c> §3-4) —
/// вход <see cref="IdealHallCalculator.Calculate"/>.
/// </summary>
public sealed record IdealHallBranchTrajectory
{
    /// <summary>Код сектора.</summary>
    public required string SectorId { get; init; }

    /// <summary>Отображаемое имя сектора.</summary>
    public required string SectorName { get; init; }

    /// <summary>
    /// X(t) по ходам: индекс 0 — ход 1, индекс <c>Count-1</c> — последний просчитанный ход. Растёт
    /// полого в начале (дёшево ошибиться), круче к концу (дорого простаивать на дорогом переделе) —
    /// см. <c>docs/production-balance.md</c> §3.
    /// </summary>
    public required IReadOnlyList<decimal> ValueByTurn { get; init; }

    /// <summary>
    /// Накопленный за всю траекторию расход по категориям — теми же <see
    /// cref="FinanceHistoryCalculator.OperationType"/>, которыми движок разбирает собственный журнал.
    /// Существует ради одной проверки (<c>IdealHallEngineReconciliationTests</c>, 2026-09-07): на
    /// одинаковом сценарии идеальный зал и настоящий тик обязаны списать одно и то же по каждой
    /// статье. Без неё сверять было нечего — зал отдавал только итоговое X(t), в котором пропущенная
    /// статья расходов неотличима от честного расчёта, и ровно так три дефекта учёта прожили в
    /// проекте несколько недель (<c>docs/economy-accounting-audit.md</c>).
    ///
    /// <para>
    /// Не полный список категорий движка: капремонта здесь нет никогда (износ не моделируется, см.
    /// doc-comment <see cref="IdealHallCalculator"/>), аварийной закупки — тоже (зал по построению
    /// никогда не остаётся без сырья).
    /// </para>
    /// </summary>
    public required IReadOnlyDictionary<FinanceHistoryCalculator.OperationType, decimal> ExpensesByType { get; init; }
}
