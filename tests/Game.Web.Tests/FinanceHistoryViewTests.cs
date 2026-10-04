using Game.Engine;
using static Game.Engine.FinanceHistoryCalculator;

namespace Game.Web.Tests;

/// <summary>
/// История операций «Аналитики» (docs/manager-ui/README.md §4, блок 5): группы по ходам, свежие сверху,
/// итог хода со знаком, фильтр по ходу и по фабрике.
/// </summary>
public class FinanceHistoryViewTests
{
    private static readonly Ulid Mine = Ulid.NewUlid();
    private static readonly Ulid Mill = Ulid.NewUlid();

    private static FinanceOperation Op(int turn, OperationType type, MoneyDirection direction, decimal amount, Ulid? factory = null) =>
        new(DateTimeOffset.UnixEpoch, turn, type, direction, amount, Rate: null, factory);

    private static readonly IReadOnlyList<FinanceOperation> Operations =
    [
        Op(1, OperationType.FactoryBuilt, MoneyDirection.Expense, 500m, Mine),
        Op(2, OperationType.SalariesPaid, MoneyDirection.Expense, 100m, Mine),
        Op(2, OperationType.SalariesPaid, MoneyDirection.Expense, 80m, Mill),
        Op(2, OperationType.MaterialSold, MoneyDirection.Income, 300m),
    ];

    [Fact]
    public void Turns_Go_Newest_First_With_A_Signed_Net()
    {
        var groups = FinanceHistoryView.Build(Operations, turn: null, factoryId: null);

        Assert.Equal([2, 1], groups.Select(g => g.Turn));
        Assert.Equal(120m, groups[0].Net);
        Assert.Equal(-500m, groups[1].Net);
        // Внутри хода — свежие сверху.
        Assert.Equal(OperationType.MaterialSold, groups[0].Operations[0].Type);
    }

    [Fact]
    public void A_Turn_Filter_Keeps_Only_That_Turn()
    {
        var group = Assert.Single(FinanceHistoryView.Build(Operations, turn: 1, factoryId: null));

        Assert.Equal(1, group.Turn);
    }

    [Fact]
    public void A_Factory_Filter_Totals_Only_That_Factorys_Operations()
    {
        var groups = FinanceHistoryView.Build(Operations, turn: null, factoryId: Mine);

        Assert.Equal([-100m, -500m], groups.Select(g => g.Net));
        Assert.All(groups.SelectMany(g => g.Operations), op => Assert.Equal(Mine, op.FactoryId));
    }

    [Fact]
    public void Signed_Amounts_Read_With_A_Real_Minus()
    {
        Assert.StartsWith("+", FinanceHistoryView.FormatSigned(120m));
        Assert.StartsWith("−", FinanceHistoryView.FormatSigned(-500m));
    }
}
