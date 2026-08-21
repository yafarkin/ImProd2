using Game.Domain;

namespace Game.Engine.Tests;

public class ContractEventsTests
{
    private static ContractSpec SheetSpot(Ulid buyerId, Ulid sellerId, decimal volume = 10m, decimal unitPrice = 20m, decimal penaltyRate = 0.1m)
    {
        var terms = new ContractTerms(
            ContractType.Spot, TestGameConfig.Sheet, volume, unitPrice, penaltyRate,
            effectiveTurn: 1, spotDeliveryTurn: 1, recurringEndTurn: null);
        var contract = new Contract(Ulid.NewUlid(), buyerId, sellerId, terms, "ABC123");

        return ContractSpec.From(contract);
    }

    private static (EventLog<GameSessionState> Log, Team Buyer, Team Seller, ContractSpec Spec) SignAndConfirm(
        decimal volume = 10m, decimal unitPrice = 20m, decimal penaltyRate = 0.1m)
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var spec = SheetSpot(buyer.Id, seller.Id, volume, unitPrice, penaltyRate);

        log.Append(new ContractSigned { Id = Ulid.NewUlid(), Contract = spec });
        log.Append(new ContractConfirmed { Id = Ulid.NewUlid(), ContractId = spec.ContractId, ConfirmingTeamId = buyer.Id });

        return (log, buyer, seller, spec);
    }

    [Fact]
    public void ContractSigned_Adds_A_Pending_Contract_Resolved_Against_The_Session_Catalog()
    {
        var (log, buyer, seller) = TestGameConfig.StartSessionWithTwoTeams();
        var spec = SheetSpot(buyer.Id, seller.Id);

        log.Append(new ContractSigned { Id = Ulid.NewUlid(), Contract = spec });

        var contract = log.State.Contracts[spec.ContractId];
        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
        Assert.Same(TestGameConfig.Sheet, contract.Terms.Material); // канонический экземпляр из каталога
    }

    [Fact]
    public void ContractConfirmed_Activates_The_Contract()
    {
        var (log, _, _, spec) = SignAndConfirm();

        Assert.Equal(ContractStatus.Active, log.State.Contracts[spec.ContractId].Status);
    }

    [Fact]
    public void ContractDelivered_Moves_Material_Seller_To_Buyer_And_Money_Buyer_To_Seller()
    {
        var (log, buyer, seller, spec) = SignAndConfirm(volume: 10m, unitPrice: 20m);
        seller.Warehouse.Add(TestGameConfig.Sheet, 10m, 0m);

        log.Append(new ContractDelivered { Id = Ulid.NewUlid(), ContractId = spec.ContractId, Turn = 1 });

        Assert.Equal(0m, seller.Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(10m, buyer.Warehouse.QuantityOf(TestGameConfig.Sheet));
        Assert.Equal(-200m, buyer.Balance); // 10 * 20 списано — баланс уходит в минус, это не ошибка
        Assert.Equal(200m, seller.Balance);
        Assert.Equal(ContractStatus.Completed, log.State.Contracts[spec.ContractId].Status); // spot завершён
    }

    [Fact]
    public void DeliveryMissed_Charges_The_Seller_A_Penalty_Paid_To_The_Buyer()
    {
        var (log, buyer, seller, spec) = SignAndConfirm(volume: 10m, unitPrice: 20m, penaltyRate: 0.1m);

        // штраф = 10 * 20 * 0.1 = 20
        log.Append(new DeliveryMissed { Id = Ulid.NewUlid(), ContractId = spec.ContractId, Turn = 1, ShortfallVolume = 10m, PenaltyAmount = 20m });

        Assert.Equal(-20m, seller.Balance);
        Assert.Equal(20m, buyer.Balance);
        Assert.Equal(ContractStatus.Completed, log.State.Contracts[spec.ContractId].Status); // spot: единственная поставка сорвана
    }

    [Fact]
    public void ContractTerminated_Mutual_Charges_No_Fee()
    {
        var (log, _, _, spec) = SignAndConfirm();
        var buyerBalanceBefore = log.State.Teams[spec.BuyerTeamId].Balance;

        log.Append(new ContractTerminated
        {
            Id = Ulid.NewUlid(), ContractId = spec.ContractId, Turn = 1,
            Reason = ContractTerminationReason.Mutual, TerminatingTeamId = null, Fee = 0m,
        });

        Assert.Equal(ContractStatus.Terminated, log.State.Contracts[spec.ContractId].Status);
        Assert.Equal(buyerBalanceBefore, log.State.Teams[spec.BuyerTeamId].Balance);
    }

    [Fact]
    public void ContractTerminated_Voluntary_Charges_The_Initiator_The_Fee()
    {
        var (log, buyer, _, spec) = SignAndConfirm();

        log.Append(new ContractTerminated
        {
            Id = Ulid.NewUlid(), ContractId = spec.ContractId, Turn = 1,
            Reason = ContractTerminationReason.Voluntary, TerminatingTeamId = buyer.Id, Fee = 1000m,
        });

        Assert.Equal(ContractStatus.Terminated, log.State.Contracts[spec.ContractId].Status);
        Assert.Equal(-1000m, buyer.Balance);
    }

    [Fact]
    public void EmergencyPurchased_Adds_Material_And_Debits_The_Cost()
    {
        var (log, buyer, _) = TestGameConfig.StartSessionWithTwoTeams();

        log.Append(new EmergencyPurchased
        {
            Id = Ulid.NewUlid(), Turn = 1, TeamId = buyer.Id, MaterialId = "ore", Volume = 5m, UnitPrice = 20m, TotalCost = 100m,
        });

        Assert.Equal(5m, buyer.Warehouse.QuantityOf(TestGameConfig.Ore));
        Assert.Equal(-100m, buyer.Balance);
    }
}
