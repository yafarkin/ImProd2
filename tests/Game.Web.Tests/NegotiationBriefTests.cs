using Game.Domain;

namespace Game.Web.Tests;

/// <summary>
/// «С чем идти в зал» переговорщика (docs/manager-ui/README.md §4, блок 6): свободный остаток — склад
/// минус то, что заберут свои фабрики за ход, и минус обещанное по действующим контрактам.
/// </summary>
public class NegotiationBriefTests
{
    private static readonly Ulid Us = Ulid.NewUlid();
    private static readonly Ulid Them = Ulid.NewUlid();
    private static readonly Sector AnySector = new("any", "Любой");
    private static readonly Material Coke = new("coke", "Кокс", AnySector, 1);
    private static readonly Material Ore = new("ore", "Железная руда", AnySector, 0);

    private static Contract Contract(Ulid buyer, Ulid seller, ContractType type, decimal volume, int? spotTurn, bool active = true)
    {
        var terms = new ContractTerms(type, Coke, volume, 0.47m, 0.1m, effectiveTurn: 1, spotDeliveryTurn: spotTurn, recurringEndTurn: null);
        var contract = new Contract(Ulid.NewUlid(), buyer, seller, terms, "CODE01", proposedByTeamId: buyer);
        if (active)
        {
            contract.ConfirmAutomatically();
        }

        return contract;
    }

    [Fact]
    public void Free_Stock_Subtracts_Own_Consumption_And_Promises_And_Never_Goes_Negative()
    {
        var offers = NegotiationBrief.Build(
            [(Coke, 100m, 0.29m), (Ore, 50m, null)],
            new Dictionary<string, decimal> { ["coke"] = 30m, ["ore"] = 80m },
            new Dictionary<string, decimal> { ["coke"] = 40m },
            new Dictionary<string, decimal> { ["coke"] = 0.4523m });

        var coke = offers.Single(o => o.Material == Coke);
        Assert.Equal(100m, coke.OnStock);
        Assert.Equal(30m, coke.Free);
        Assert.Equal(0.29m, coke.UnitCost);
        Assert.Equal(0.4523m, coke.MarketPrice);

        var ore = offers.Single(o => o.Material == Ore);
        Assert.Equal(0m, ore.Free);
        Assert.Null(ore.MarketPrice);
    }

    [Fact]
    public void Empty_Stock_Lines_Are_Not_Offered()
    {
        var offers = NegotiationBrief.Build(
            [(Coke, 0m, null)], new Dictionary<string, decimal>(), new Dictionary<string, decimal>(), new Dictionary<string, decimal>());

        Assert.Empty(offers);
    }

    [Fact]
    public void Only_Active_Contracts_Where_We_Sell_And_The_Delivery_Is_Still_Ahead_Count_As_Promised()
    {
        Contract[] contracts =
        [
            Contract(Them, Us, ContractType.Recurring, 3m, null),           // регулярный — ближайшая поставка
            Contract(Them, Us, ContractType.Spot, 40m, spotTurn: 15),        // разовый, ещё впереди
            Contract(Them, Us, ContractType.Spot, 99m, spotTurn: 10),        // разовый, ход поставки прошёл
            Contract(Us, Them, ContractType.Recurring, 7m, null),           // мы покупатель
            Contract(Them, Us, ContractType.Recurring, 5m, null, active: false), // ещё не подтверждён
        ];

        var promised = NegotiationBrief.Promised(contracts, Us, currentTurn: 12);

        Assert.Equal(43m, promised["coke"]);
    }
}
