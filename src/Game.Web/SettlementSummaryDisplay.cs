using Game.Engine;

namespace Game.Web;

/// <summary>
/// «Итоги расчёта» на разделе «Ход» (docs/manager-ui/README.md §4, блок 2): что принёс и чего стоил
/// последний расчёт хода — четыре-пять строк по статьям вместо полной истории операций. До неё ответ
/// на вопрос «что произошло за ход» можно было собрать только по графикам и таблице на десятки строк
/// (живой обход 2026-10-04). Чистая функция над уже посчитанной историей
/// (<see cref="FinanceHistoryCalculator"/>): берёт только операции расчёта, а не решения того же хода.
/// </summary>
public static class SettlementSummaryDisplay
{
    /// <summary>Строка сводки: статья и сумма со знаком (приход — плюс, расход — минус).</summary>
    public sealed record Line(string Label, decimal Amount);

    /// <summary>Сводка одного расчёта: номер хода, строки по статьям в постоянном порядке, итог и поставки по контрактам словами.</summary>
    public sealed record Summary(int Turn, IReadOnlyList<Line> Lines, decimal Net, IReadOnlyList<string> Deliveries);

    private static readonly (string Label, FinanceHistoryCalculator.OperationType[] Types)[] Categories =
    [
        ("Доходы", [
            FinanceHistoryCalculator.OperationType.MaterialSold,
            FinanceHistoryCalculator.OperationType.GrantReceived]),
        ("Зарплаты и содержание", [
            FinanceHistoryCalculator.OperationType.SalariesPaid,
            FinanceHistoryCalculator.OperationType.FactoryUpkeep,
            FinanceHistoryCalculator.OperationType.FactoryOverhead,
            FinanceHistoryCalculator.OperationType.WarehouseFee]),
        ("Наём, увольнение, капремонт", [
            FinanceHistoryCalculator.OperationType.WorkersHired,
            FinanceHistoryCalculator.OperationType.WorkersFired,
            FinanceHistoryCalculator.OperationType.FactoryOverhaul]),
        ("Аварийные закупки", [
            FinanceHistoryCalculator.OperationType.EmergencyPurchase]),
        ("Контракты", [
            FinanceHistoryCalculator.OperationType.ContractDelivery,
            FinanceHistoryCalculator.OperationType.DeliveryMissPenalty,
            FinanceHistoryCalculator.OperationType.ContractTerminationFee]),
        ("Исследования", [
            FinanceHistoryCalculator.OperationType.RndInvested,
            FinanceHistoryCalculator.OperationType.GenerationResearchInvested]),
    ];

    /// <summary>
    /// Сводка последнего расчёта, в котором у команды двигались деньги; <see langword="null"/> — расчётов
    /// с денежными операциями ещё не было. Статья попадает в сводку, только если по ней что-то было:
    /// пустые нули на телефоне только отодвигают важное.
    /// </summary>
    public static Summary? Build(IReadOnlyList<FinanceHistoryCalculator.FinanceOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var settlementOperations = operations.Where(o => o.DuringSettlement).ToList();
        if (settlementOperations.Count == 0)
        {
            return null;
        }

        var turn = settlementOperations.Max(o => o.Turn);
        var ofTurn = settlementOperations.Where(o => o.Turn == turn).ToList();

        var lines = new List<Line>();
        foreach (var (label, types) in Categories)
        {
            var inCategory = ofTurn.Where(o => types.Contains(o.Type)).ToList();
            if (inCategory.Count > 0)
            {
                lines.Add(new Line(label, inCategory.Sum(Signed)));
            }
        }

        var deliveries = ofTurn
            .Where(o => o.Type is FinanceHistoryCalculator.OperationType.ContractDelivery
                or FinanceHistoryCalculator.OperationType.DeliveryMissPenalty)
            .Select(DeliveryText)
            .ToList();

        return new Summary(turn, lines, ofTurn.Sum(Signed), deliveries);
    }

    private static decimal Signed(FinanceHistoryCalculator.FinanceOperation operation) =>
        operation.Direction == FinanceHistoryCalculator.MoneyDirection.Income ? operation.Amount : -operation.Amount;

    /// <summary>Поставка словами — что, сколько, от кого или кому; срыв — сколько не хватило и штраф.</summary>
    private static string DeliveryText(FinanceHistoryCalculator.FinanceOperation operation)
    {
        var volume = operation.Volume is { } v ? v.ToString("0.##") : "?";
        var weReceive = operation.Direction == FinanceHistoryCalculator.MoneyDirection.Expense;

        if (operation.Type == FinanceHistoryCalculator.OperationType.ContractDelivery)
        {
            return weReceive
                ? $"получено {volume} ед. «{operation.MaterialName}» от команды «{operation.CounterpartyName}»"
                : $"отгружено {volume} ед. «{operation.MaterialName}» команде «{operation.CounterpartyName}»";
        }

        // Штраф: расход — срыв по нашей вине, приход — компенсация от поставщика.
        return weReceive
            ? $"срыв поставки «{operation.MaterialName}» команде «{operation.CounterpartyName}»: не хватило {volume} ед., штраф {DashboardDisplay.FormatMoney(operation.Amount)}"
            : $"команда «{operation.CounterpartyName}» не довезла {volume} ед. «{operation.MaterialName}», компенсация {DashboardDisplay.FormatMoney(operation.Amount)}";
    }
}
