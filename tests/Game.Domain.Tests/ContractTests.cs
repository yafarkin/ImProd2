namespace Game.Domain.Tests;

public class ContractTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    private static readonly ContractTerms Terms =
        new(ContractType.Spot, Sheet, 10m, 20m, 0.1m, effectiveTurn: 3, spotDeliveryTurn: 5, recurringEndTurn: null);

    private static Contract NewPendingContract() =>
        new(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), Terms, "ABC123");

    [Fact]
    public void Construction_Starts_In_PendingConfirmation_With_No_Termination_Reason()
    {
        var contract = NewPendingContract();

        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
        Assert.Null(contract.TerminationReason);
    }

    [Fact]
    public void Construction_Throws_When_Buyer_And_Seller_Are_The_Same_Team()
    {
        var teamId = Ulid.NewUlid();

        Assert.Throws<ArgumentException>(() => new Contract(Ulid.NewUlid(), teamId, teamId, Terms, "ABC123"));
    }

    [Fact]
    public void Confirm_By_A_Manager_Activates_The_Contract()
    {
        var contract = NewPendingContract();

        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId);

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void Confirm_By_A_Negotiator_Throws_And_Leaves_The_Contract_Pending()
    {
        var contract = NewPendingContract();

        Assert.Throws<InvalidOperationException>(() => contract.Confirm(TeamRole.Negotiator, contract.BuyerTeamId));

        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
    }

    [Fact]
    public void Confirm_By_A_Team_Not_Party_To_The_Contract_Throws()
    {
        var contract = NewPendingContract();

        Assert.Throws<InvalidOperationException>(() => contract.Confirm(TeamRole.Manager, Ulid.NewUlid()));

        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
    }

    [Fact]
    public void Confirm_By_The_Proposing_Team_Throws_Only_The_Counterparty_Can_Confirm()
    {
        var buyerId = Ulid.NewUlid();
        var sellerId = Ulid.NewUlid();
        var contract = new Contract(Ulid.NewUlid(), buyerId, sellerId, Terms, "ABC123", proposedByTeamId: buyerId);

        Assert.Throws<InvalidOperationException>(() => contract.Confirm(TeamRole.Manager, buyerId));

        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
    }

    [Fact]
    public void Confirm_By_The_Counterparty_Of_The_Proposing_Team_Activates_The_Contract()
    {
        var buyerId = Ulid.NewUlid();
        var sellerId = Ulid.NewUlid();
        var contract = new Contract(Ulid.NewUlid(), buyerId, sellerId, Terms, "ABC123", proposedByTeamId: buyerId);

        contract.Confirm(TeamRole.Manager, sellerId);

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void Confirm_Throws_When_The_Contract_Is_Already_Active()
    {
        var contract = NewPendingContract();
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId);

        Assert.Throws<InvalidOperationException>(() => contract.Confirm(TeamRole.Manager, contract.SellerTeamId));
    }

    [Fact]
    public void ConfirmAutomatically_Activates_The_Contract_Without_Checking_Sides()
    {
        var contract = NewPendingContract();

        contract.ConfirmAutomatically();

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void Terminate_An_Active_Contract_Records_The_Reason()
    {
        var contract = NewPendingContract();
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId);

        contract.Terminate(ContractTerminationReason.Mutual);

        Assert.Equal(ContractStatus.Terminated, contract.Status);
        Assert.Equal(ContractTerminationReason.Mutual, contract.TerminationReason);
    }

    [Fact]
    public void Terminate_Throws_When_The_Contract_Is_Still_Pending_Confirmation()
    {
        var contract = NewPendingContract();

        Assert.Throws<InvalidOperationException>(() => contract.Terminate(ContractTerminationReason.Voluntary));
    }

    [Fact]
    public void Terminate_Throws_When_The_Contract_Is_Already_Terminated()
    {
        var contract = NewPendingContract();
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId);
        contract.Terminate(ContractTerminationReason.Mutual);

        Assert.Throws<InvalidOperationException>(() => contract.Terminate(ContractTerminationReason.Voluntary));
    }

    [Fact]
    public void Complete_Moves_An_Active_Spot_Contract_To_Completed()
    {
        var contract = NewPendingContract(); // Terms — spot
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId);

        contract.Complete();

        Assert.Equal(ContractStatus.Completed, contract.Status);
    }

    [Fact]
    public void Complete_Throws_For_A_Recurring_Contract()
    {
        var recurringTerms = new ContractTerms(
            ContractType.Recurring, Sheet, 10m, 20m, 0.1m, effectiveTurn: 3, spotDeliveryTurn: null, recurringEndTurn: 15);
        var contract = new Contract(Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), recurringTerms, "ABC123");
        contract.Confirm(TeamRole.Manager, contract.BuyerTeamId);

        Assert.Throws<InvalidOperationException>(() => contract.Complete());
    }

    [Fact]
    public void Complete_Throws_When_The_Contract_Is_Not_Active()
    {
        var contract = NewPendingContract();

        Assert.Throws<InvalidOperationException>(() => contract.Complete());
    }
}
