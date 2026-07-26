namespace Game.Domain;

/// <summary>
/// Условия сделки, независимо введённые одной из двух сторон (SPEC §6: «обе команды вводят условия
/// независимо»). Сама по себе ни на что не влияет — договор возникает только когда две заявки от
/// разных сторон сходятся, см. <see cref="ContractFormation.TryMatch"/>.
/// </summary>
public sealed record ContractProposal
{
    /// <summary>Команда-покупатель.</summary>
    public Ulid BuyerTeamId { get; }

    /// <summary>Команда-продавец.</summary>
    public Ulid SellerTeamId { get; }

    /// <summary>Команда, подавшая именно эту заявку — обязана быть покупателем или продавцом.</summary>
    public Ulid SubmittedByTeamId { get; }

    /// <summary>Условия, как их видит подавшая заявку сторона.</summary>
    public ContractTerms Terms { get; }

    public ContractProposal(Ulid buyerTeamId, Ulid sellerTeamId, Ulid submittedByTeamId, ContractTerms terms)
    {
        if (buyerTeamId == Ulid.Empty)
        {
            throw new ArgumentException("Buyer team id must not be empty.", nameof(buyerTeamId));
        }
        if (sellerTeamId == Ulid.Empty)
        {
            throw new ArgumentException("Seller team id must not be empty.", nameof(sellerTeamId));
        }
        if (buyerTeamId == sellerTeamId)
        {
            throw new ArgumentException("A team cannot contract with itself.", nameof(sellerTeamId));
        }
        if (submittedByTeamId != buyerTeamId && submittedByTeamId != sellerTeamId)
        {
            throw new ArgumentException(
                "The submitting team must be either the buyer or the seller.", nameof(submittedByTeamId));
        }
        ArgumentNullException.ThrowIfNull(terms);

        BuyerTeamId = buyerTeamId;
        SellerTeamId = sellerTeamId;
        SubmittedByTeamId = submittedByTeamId;
        Terms = terms;
    }
}
