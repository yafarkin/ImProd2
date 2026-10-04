using Game.Domain;

namespace Game.Web.Tests;

/// <summary>
/// Доска потребностей по материалам (docs/manager-ui/README.md §4, блок 4): спрос и предложение по
/// одному товару — в одной группе, сначала то, что касается нас.
/// </summary>
public class NeedsBoardTests
{
    private static readonly Ulid Us = Ulid.NewUlid();
    private static readonly Ulid Oil = Ulid.NewUlid();
    private static readonly Ulid Wood = Ulid.NewUlid();

    private static readonly Sector AnySector = new("any", "Любой");
    private static readonly Material Coke = new("coke", "Кокс", AnySector, 1);
    private static readonly Material Gas = new("gas", "Природный газ", AnySector, 0);
    private static readonly Material Boards = new("boards", "Пиломатериал", AnySector, 1);

    private static readonly IReadOnlyDictionary<Ulid, string> Names = new Dictionary<Ulid, string>
    {
        [Us] = "Сталь", [Oil] = "Нефть", [Wood] = "Лес",
    };

    private static NeedPosting Need(Ulid team, Material material, NeedDirection direction, string? comment = null) =>
        new(Ulid.NewUlid(), team, material, direction, NeedVolumeOrder.Medium, comment);

    private static IReadOnlyList<NeedsBoard.MaterialGroup> Build(
        IReadOnlyList<NeedPosting> needs,
        IReadOnlyDictionary<string, decimal>? stock = null,
        IReadOnlySet<string>? needed = null) =>
        NeedsBoard.Build(needs, Names, Us, stock ?? new Dictionary<string, decimal>(), needed ?? new HashSet<string>());

    [Fact]
    public void Demand_And_Supply_For_One_Material_Share_A_Group()
    {
        var board = Build([Need(Oil, Gas, NeedDirection.Surplus), Need(Wood, Gas, NeedDirection.Deficit), Need(Wood, Boards, NeedDirection.Surplus)]);

        var gas = board.Single(g => g.Material == Gas);
        Assert.Equal(2, gas.Entries.Count);
        Assert.Contains(gas.Entries, e => e.Direction == NeedDirection.Surplus && e.TeamName == "Нефть");
        Assert.Contains(gas.Entries, e => e.Direction == NeedDirection.Deficit && e.TeamName == "Лес");
    }

    [Fact]
    public void Materials_That_Concern_Us_Come_First_Then_Alphabetically()
    {
        // Газ и пиломатериал нас не касаются, кокс лежит у нас на складе — он первый, хоть и не первый по алфавиту.
        var board = Build(
            [Need(Oil, Gas, NeedDirection.Surplus), Need(Wood, Boards, NeedDirection.Surplus), Need(Oil, Coke, NeedDirection.Deficit)],
            stock: new Dictionary<string, decimal> { ["coke"] = 1642m });

        Assert.Equal(["Кокс", "Пиломатериал", "Природный газ"], board.Select(g => g.Material.Name));
        Assert.Equal(1642m, board[0].OurStock);
        Assert.True(board[0].ConcernsUs);
        Assert.False(board[1].ConcernsUs);
    }

    [Fact]
    public void A_Material_Our_Factories_Need_Or_Our_Own_Entry_Also_Concerns_Us()
    {
        var board = Build(
            [Need(Oil, Gas, NeedDirection.Surplus), Need(Us, Boards, NeedDirection.Deficit)],
            needed: new HashSet<string> { "gas" });

        Assert.All(board, group => Assert.True(group.ConcernsUs));
        Assert.True(board.Single(g => g.Material == Gas).NeededByUs);
        Assert.True(board.Single(g => g.Material == Boards).Entries.Single().IsOurs);
    }

    [Fact]
    public void Our_Entries_Lead_Their_Group_And_A_Blank_Comment_Is_Dropped()
    {
        var board = Build([Need(Wood, Coke, NeedDirection.Deficit, "  "), Need(Us, Coke, NeedDirection.Surplus, "склад забит")]);

        var entries = board.Single().Entries;
        Assert.True(entries[0].IsOurs);
        Assert.Equal("склад забит", entries[0].Comment);
        Assert.Null(entries[1].Comment);
    }

    [Theory]
    [InlineData(NeedDirection.Deficit, false, "ищет")]
    [InlineData(NeedDirection.Deficit, true, "ищем")]
    [InlineData(NeedDirection.Surplus, false, "предлагает")]
    [InlineData(NeedDirection.Surplus, true, "предлагаем")]
    public void Direction_Reads_As_A_Verb(NeedDirection direction, bool ours, string expected) =>
        Assert.Equal(expected, NeedsBoard.DirectionVerb(direction, ours));
}
