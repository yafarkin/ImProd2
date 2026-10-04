using Game.Engine;
using Op = Game.Engine.FinanceHistoryCalculator.OperationType;
using Dir = Game.Engine.FinanceHistoryCalculator.MoneyDirection;

namespace Game.Web.Tests;

/// <summary>«Итоги расчёта» на разделе «Ход» (блок 2 редизайна): сводка последнего расчёта по статьям.</summary>
public class SettlementSummaryDisplayTests
{
    private static FinanceHistoryCalculator.FinanceOperation Op_(int turn, Op type, Dir direction, decimal amount, bool settlement = true,
        string? material = null, decimal? volume = null, string? counterparty = null) =>
        new(DateTimeOffset.UnixEpoch, turn, type, direction, amount, Rate: null,
            MaterialName: material, Volume: volume, CounterpartyName: counterparty, DuringSettlement: settlement);

    [Fact]
    public void No_Settlement_Yet_Means_No_Summary()
    {
        Assert.Null(SettlementSummaryDisplay.Build([Op_(1, Op.FactoryBuilt, Dir.Expense, 1000m, settlement: false)]));
    }

    [Fact]
    public void Only_The_Latest_Settlement_Counts_And_Decisions_Of_The_Same_Turn_Are_Left_Out()
    {
        var summary = SettlementSummaryDisplay.Build(
        [
            Op_(3, Op.SalariesPaid, Dir.Expense, 100m),
            Op_(4, Op.SalariesPaid, Dir.Expense, 120m),
            Op_(4, Op.FactoryUpkeep, Dir.Expense, 30m),
            Op_(4, Op.MaterialSold, Dir.Income, 400m),
            Op_(4, Op.GenerationResearchInvested, Dir.Expense, 125m),
            Op_(4, Op.FactoryBuilt, Dir.Expense, 1410m, settlement: false), // постройка уже после расчёта
        ])!;

        Assert.Equal(4, summary.Turn);
        Assert.Equal(
            [("Доходы", 400m), ("Зарплаты и содержание", -150m), ("Исследования", -125m)],
            summary.Lines.Select(l => (l.Label, l.Amount)));
        Assert.Equal(125m, summary.Net);
    }

    [Fact]
    public void Contract_Deliveries_Are_Told_In_Words()
    {
        var summary = SettlementSummaryDisplay.Build(
        [
            Op_(6, Op.ContractDelivery, Dir.Expense, 1.5m, material: "Природный газ", volume: 3m, counterparty: "Нефть"),
            Op_(6, Op.DeliveryMissPenalty, Dir.Expense, 40m, material: "Кокс", volume: 10m, counterparty: "Лес"),
        ])!;

        Assert.Equal(
        [
            "получено 3 ед. «Природный газ» от команды «Нефть»",
            "срыв поставки «Кокс» команде «Лес»: не хватило 10 ед., штраф 40 ¤",
        ], summary.Deliveries);
        Assert.Equal([("Контракты", -41.5m)], summary.Lines.Select(l => (l.Label, l.Amount)));
    }
}
