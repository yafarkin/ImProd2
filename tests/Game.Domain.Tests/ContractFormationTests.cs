namespace Game.Domain.Tests;

public class ContractFormationTests
{
    private static readonly Sector SectorA = new("A", "Металлургия");
    private static readonly Material Sheet = new("sheet", "Стальные листы", SectorA, level: 1);

    private static readonly Ulid BuyerId = Ulid.NewUlid();
    private static readonly Ulid SellerId = Ulid.NewUlid();

    private static ContractTerms Terms(decimal volume = 10m) =>
        new(ContractType.Spot, Sheet, volume, 20m, 0.1m, effectiveTurn: 3, spotDeliveryTurn: 5, recurringEndTurn: null);

    [Fact]
    public void TryMatch_With_Matching_Proposals_Creates_A_Pending_Contract_With_A_Code()
    {
        var terms = Terms();
        var buyerProposal = new ContractProposal(BuyerId, SellerId, BuyerId, terms);
        var sellerProposal = new ContractProposal(BuyerId, SellerId, SellerId, terms);
        var contractId = Ulid.NewUlid();

        var result = ContractFormation.TryMatch(buyerProposal, sellerProposal, contractId, new Random(1));

        Assert.True(result.IsMatched);
        Assert.Empty(result.Mismatches);
        var contract = result.Contract!;
        Assert.Equal(contractId, contract.Id);
        Assert.Equal(BuyerId, contract.BuyerTeamId);
        Assert.Equal(SellerId, contract.SellerTeamId);
        Assert.Equal(terms, contract.Terms);
        Assert.Equal(ContractStatus.PendingConfirmation, contract.Status);
        Assert.Equal(6, contract.ConfirmationCode.Length);
    }

    [Fact]
    public void TryMatch_Reports_CounterpartiesDiffer_When_Buyer_Or_Seller_Do_Not_Match()
    {
        var terms = Terms();
        var buyerProposal = new ContractProposal(BuyerId, SellerId, BuyerId, terms);
        var thirdParty = Ulid.NewUlid();
        var sellerProposal = new ContractProposal(BuyerId, thirdParty, thirdParty, terms);

        var result = ContractFormation.TryMatch(buyerProposal, sellerProposal, Ulid.NewUlid(), new Random(1));

        Assert.False(result.IsMatched);
        Assert.Null(result.Contract);
        Assert.Contains(ContractMismatchReason.CounterpartiesDiffer, result.Mismatches);
    }

    [Fact]
    public void TryMatch_Reports_SubmittedByTheSameTeam_When_Both_Proposals_Come_From_One_Side()
    {
        var terms = Terms();
        var buyerProposal = new ContractProposal(BuyerId, SellerId, BuyerId, terms);
        var anotherFromBuyer = new ContractProposal(BuyerId, SellerId, BuyerId, terms);

        var result = ContractFormation.TryMatch(buyerProposal, anotherFromBuyer, Ulid.NewUlid(), new Random(1));

        Assert.False(result.IsMatched);
        Assert.Contains(ContractMismatchReason.SubmittedByTheSameTeam, result.Mismatches);
    }

    [Fact]
    public void TryMatch_Reports_TermsDiffer_When_Volumes_Disagree()
    {
        var buyerProposal = new ContractProposal(BuyerId, SellerId, BuyerId, Terms(volume: 10m));
        var sellerProposal = new ContractProposal(BuyerId, SellerId, SellerId, Terms(volume: 12m));

        var result = ContractFormation.TryMatch(buyerProposal, sellerProposal, Ulid.NewUlid(), new Random(1));

        Assert.False(result.IsMatched);
        Assert.Contains(ContractMismatchReason.TermsDiffer, result.Mismatches);
    }

    [Fact]
    public void TryMatch_Reports_Every_Applicable_Mismatch_At_Once()
    {
        var buyerProposal = new ContractProposal(BuyerId, SellerId, BuyerId, Terms(volume: 10m));
        // Тот же покупатель "подаёт" вторую заявку под видом продавца с другими условиями —
        // расходятся и стороны, и подача от одной и той же команды, и сами условия.
        var conflicting = new ContractProposal(SellerId, BuyerId, BuyerId, Terms(volume: 12m));

        var result = ContractFormation.TryMatch(buyerProposal, conflicting, Ulid.NewUlid(), new Random(1));

        Assert.False(result.IsMatched);
        Assert.Contains(ContractMismatchReason.CounterpartiesDiffer, result.Mismatches);
        Assert.Contains(ContractMismatchReason.SubmittedByTheSameTeam, result.Mismatches);
        Assert.Contains(ContractMismatchReason.TermsDiffer, result.Mismatches);
    }
}
