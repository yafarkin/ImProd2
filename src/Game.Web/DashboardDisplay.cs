using Game.Config.Session;
using Game.Domain;
using Game.Engine;

namespace Game.Web;

/// <summary>
/// Форматирование дашборда команды (Блок 9.1, SPEC §9.3) — по тому же принципу, что и
/// <see cref="PhaseDisplay"/>: чистые статические функции над уже посчитанным состоянием сессии,
/// без собственного хранимого состояния.
/// </summary>
public static class DashboardDisplay
{
    /// <summary>Денежная сумма для отображения на экране.</summary>
    public static string FormatMoney(decimal amount) => $"{amount:N0} ₽";

    /// <summary>Процентная ставка для отображения на экране (Блок 9.2).</summary>
    public static string FormatRate(decimal rate) => rate.ToString("P1");

    /// <summary>Предпросмотр ставки и платежа за ход для гипотетического займа (Блок 9.2, SPEC §5.9:
    /// «в UI до подтверждения — расчёт платежа за ход») — до того, как команда его подтвердила.</summary>
    public static (decimal Rate, decimal Payment) PreviewLoan(
        decimal currentDebt, decimal penaltyRateSurcharge, decimal reputationPercentage,
        decimal additionalAmount, StartingConditionsConfig loanConfig)
    {
        var projectedDebt = currentDebt + additionalAmount;
        var rate = FinanceCalculator.CalculateEffectiveLoanRate(
            projectedDebt, penaltyRateSurcharge, reputationPercentage, loanConfig);

        return (rate, rate * projectedDebt);
    }

    /// <summary>Русская подпись статуса контракта для дашборда («что я обещал другим»).</summary>
    public static string ContractStatusLabel(ContractStatus status) => status switch
    {
        ContractStatus.PendingConfirmation => "Ждёт подтверждения",
        ContractStatus.Active => "Действует",
        ContractStatus.Completed => "Исполнен",
        ContractStatus.Terminated => "Расторгнут",
        _ => status.ToString()
    };

    /// <summary>
    /// Пытается посчитать себестоимость единицы материала (<see cref="CostCalculator.CalculateUnitCost"/>),
    /// используя текущие рыночные котировки сырья как базовую цену. Возвращает <c>false</c>, если
    /// котировки для какого-то из видов сырья в цепочке ещё нет (например, самый первый ход до
    /// первого <see cref="MarketUpdated"/>) — дашборд в этом случае просто не показывает число, а не падает.
    /// </summary>
    public static bool TryCalculateUnitCost(Material product, GameSessionState state, out decimal unitCost)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(state);

        var rawMaterialCosts = state.Config.Materials.Values
            .Where(m => m.IsRawMaterial && state.Market.HasQuote(m.Id))
            .ToDictionary(m => m, m => state.Market.QuoteOf(m.Id).Price);

        try
        {
            unitCost = CostCalculator.CalculateUnitCost(product, state.Config.RecipeBook, rawMaterialCosts);
            return true;
        }
        catch (ArgumentException)
        {
            unitCost = 0m;
            return false;
        }
    }

    /// <summary>Один уровень пирамиды входов — материал, количество и глубина от корня (0 — сам продукт).</summary>
    public sealed record PyramidRow(Material Material, decimal Quantity, int Depth);

    /// <summary>
    /// Разворачивает пирамиду входов (<see cref="CostCalculator.BuildInputPyramid"/>) в плоский
    /// предпорядковый список — Razor не может естественно рекурсировать шаблон без отдельного
    /// компонента, а плоский список с глубиной проще отрисовать отступами.
    /// </summary>
    public static IReadOnlyList<PyramidRow> FlattenPyramid(InputPyramidNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var rows = new List<PyramidRow>();
        Flatten(root, 0, rows);
        return rows;
    }

    private static void Flatten(InputPyramidNode node, int depth, List<PyramidRow> rows)
    {
        rows.Add(new PyramidRow(node.Material, node.Quantity, depth));
        foreach (var input in node.Inputs)
        {
            Flatten(input, depth + 1, rows);
        }
    }
}
