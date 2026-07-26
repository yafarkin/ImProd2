namespace Game.Domain.Tests;

public class ContractProposalTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    private static readonly ContractTerms Terms =
        new(ContractType.Spot, Sheet, 10m, 20m, 0.1m, effectiveTurn: 3, spotDeliveryTurn: 5, recurringEndTurn: null);

    private static readonly Ulid BuyerId = Ulid.NewUlid();
    private static readonly Ulid SellerId = Ulid.NewUlid();

    [Fact]
    public void Construction_Succeeds_When_Submitted_By_The_Buyer()
    {
        var proposal = new ContractProposal(BuyerId, SellerId, BuyerId, Terms);

        Assert.Equal(BuyerId, proposal.SubmittedByTeamId);
    }

    [Fact]
    public void Construction_Succeeds_When_Submitted_By_The_Seller()
    {
        var proposal = new ContractProposal(BuyerId, SellerId, SellerId, Terms);

        Assert.Equal(SellerId, proposal.SubmittedByTeamId);
    }

    [Fact]
    public void Construction_Throws_When_Submitted_By_A_Third_Party_Team()
    {
        var thirdParty = Ulid.NewUlid();

        Assert.Throws<ArgumentException>(() => new ContractProposal(BuyerId, SellerId, thirdParty, Terms));
    }

    [Fact]
    public void Construction_Throws_When_Buyer_And_Seller_Are_The_Same_Team()
    {
        Assert.Throws<ArgumentException>(() => new ContractProposal(BuyerId, BuyerId, BuyerId, Terms));
    }

    [Fact]
    public void Construction_Throws_When_Buyer_Id_Is_Empty()
    {
        Assert.Throws<ArgumentException>(() => new ContractProposal(Ulid.Empty, SellerId, SellerId, Terms));
    }
}
