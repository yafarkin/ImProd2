using Game.Engine;

namespace Game.Web;

/// <summary>
/// История операций «Аналитики» (docs/manager-ui/README.md §4, блок 5, макет mockups/analytics.html):
/// фильтр по ходу и по фабрике, операции сгруппированы по ходам, свежие сверху, итог хода — первой
/// строкой группы. Раньше это была одна сплошная таблица на сотни строк, где итог хода приходилось
/// складывать в уме.
/// </summary>
public static class FinanceHistoryView
{
    /// <summary>Операции одного хода и их итог со знаком: плюс — заработали, минус — потратили.</summary>
    public sealed record TurnGroup(int Turn, decimal Net, IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> Operations);

    /// <summary>
    /// <paramref name="turn"/> — только этот ход, <c>null</c> — все; <paramref name="factoryId"/> —
    /// только операции этой фабрики, <c>null</c> — все. Итог группы считается по тому, что прошло
    /// фильтр: при фильтре по фабрике это итог именно её операций за ход.
    /// </summary>
    public static IReadOnlyList<TurnGroup> Build(
        IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> operations, int? turn, Ulid? factoryId)
    {
        ArgumentNullException.ThrowIfNull(operations);

        return operations
            .Where(operation => turn is null || operation.Turn == turn)
            .Where(operation => factoryId is null || operation.FactoryId == factoryId)
            .GroupBy(operation => operation.Turn)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                // Внутри хода — свежие сверху, как и раньше в сплошной таблице.
                var ordered = group.Reverse().ToList();
                return new TurnGroup(group.Key, ordered.Sum(Signed), ordered);
            })
            .ToList();
    }

    /// <summary>Сумма операции со знаком направления.</summary>
    public static decimal Signed(FinanceHistoryCalculator.FinanceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation.Direction == FinanceHistoryCalculator.MoneyDirection.Income ? operation.Amount : -operation.Amount;
    }

    /// <summary>«+612 ¤» / «−325 ¤» — со знаком, как в «Итогах расчёта».</summary>
    public static string FormatSigned(decimal amount) =>
        (amount < 0 ? "−" : "+") + DashboardDisplay.FormatMoney(Math.Abs(amount));
}
