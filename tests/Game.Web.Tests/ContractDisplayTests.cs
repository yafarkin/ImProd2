using Game.Domain;
using Game.Engine;

namespace Game.Web.Tests;

/// <summary>
/// Подписи карточек «Сделок» (docs/manager-ui/README.md §4, блок 4) и подсчёт поставок по журналу.
/// Главное — цена за единицу без округления до целого: при ценах 0.07–0.6 ¤ «1 ¤» вместо 0.5 ¤
/// искажало сделку вдвое (§2 п.15).
/// </summary>
public class ContractDisplayTests
{
    private static TeamScreen.ContractRow Row(ContractType type, decimal volume, decimal price, int? endTurn = null) => new(
        Ulid.NewUlid(), "Покупатель", "Нефть", "Природный газ", type, volume, price, 0.1m,
        ContractStatus.Active, "Действует", "8ZKPX2", EffectiveTurn: 6, SpotDeliveryTurn: type == ContractType.Spot ? 9 : null,
        RecurringEndTurn: endTurn, HasPendingRevision: false, PendingRevisionIsMine: false,
        PendingVolume: null, PendingUnitPrice: null, PendingPenaltyRate: null, PendingRecurringEndTurn: null, WeAreTheProposer: false);

    [Fact]
    public void A_Recurring_Contract_Shows_Unit_Price_And_Sum_Per_Turn_Without_Rounding()
    {
        var line = ContractDisplay.TermsLine(Row(ContractType.Recurring, 3m, 0.5m));

        Assert.StartsWith("3 ед./ход × 0.5 ¤ = 1.5 ¤/ход · штраф ", line);
        Assert.Contains("с хода 6, бессрочно", line);
        Assert.EndsWith("· код 8ZKPX2", line);
    }

    [Fact]
    public void A_Spot_Contract_Has_No_Per_Turn_Suffix()
    {
        var line = ContractDisplay.TermsLine(Row(ContractType.Spot, 40m, 0.47m));

        Assert.StartsWith("40 ед. × 0.47 ¤ = 18.8 ¤ · ", line);
        Assert.Contains("поставка на ходу 9", line);
    }

    [Theory]
    [InlineData(7, 0, "Поставки: 7 в срок")]
    [InlineData(5, 2, "Поставки: 5 в срок, 2 сорвано")]
    public void Deliveries_Line_Names_Misses_Only_When_There_Are_Any(int delivered, int missed, string expected) =>
        Assert.Equal(expected, ContractDisplay.DeliveriesLine(new TeamScreen.DeliveryStats(delivered, missed)));

    [Fact]
    public void An_Outgoing_Proposal_Shows_Our_Own_Terms()
    {
        var line = ContractDisplay.OutgoingProposalLine(new TeamScreen.ProposalRow(
            Ulid.NewUlid(), Incoming: false, "Лес", "Железная руда", 40m, 0.47m, SubmittedOnTurn: 12, WeAreBuyer: false));

        Assert.Equal("Железная руда · 40 ед. × 0.47 ¤ · подана на ходу 12", line);
    }

    [Fact]
    public void Deliveries_Are_Counted_Per_Contract_From_The_Journal()
    {
        var first = Ulid.NewUlid();
        var second = Ulid.NewUlid();
        Change<GameSessionState>[] changes =
        [
            new ContractDelivered { Id = Ulid.NewUlid(), ContractId = first, Turn = 2 },
            new ContractDelivered { Id = Ulid.NewUlid(), ContractId = first, Turn = 3 },
            new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = first, Turn = 4, ShortfallVolume = 1m, PenaltyAmount = 0.1m },
            new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = second, Turn = 4, ShortfallVolume = 1m, PenaltyAmount = 0.1m },
        ];
        var entries = changes.Select((change, index) => new EventLogEntry<GameSessionState>
        {
            SequenceNumber = index,
            Change = change,
            Timestamp = DateTimeOffset.UnixEpoch,
            PreviousHash = "prev",
            Hash = "hash",
        }).ToList();

        var stats = TeamScreen.CountDeliveries(entries);

        Assert.Equal(new TeamScreen.DeliveryStats(2, 1), stats[first]);
        Assert.Equal(new TeamScreen.DeliveryStats(0, 1), stats[second]);
    }
}
