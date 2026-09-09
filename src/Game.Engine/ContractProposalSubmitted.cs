using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Команда подала свою половину условий сделки и ждёт встречной заявки от контрагента (SPEC §6,
/// <c>docs/TODO.md</c> №16). Отдельный факт от <see cref="ContractSigned"/>: заявка сама по себе
/// ничего не обязывает и может так и не сойтись — контракт возникает только когда сходятся две.
/// </summary>
public sealed record ContractProposalSubmitted : Change<GameSessionState>
{
    /// <summary>Идентификатор заявки.</summary>
    public required Ulid ProposalId { get; init; }

    /// <summary>Команда-покупатель.</summary>
    public required Ulid BuyerTeamId { get; init; }

    /// <summary>Команда-продавец.</summary>
    public required Ulid SellerTeamId { get; init; }

    /// <summary>Команда, подавшая заявку.</summary>
    public required Ulid SubmittedByTeamId { get; init; }

    /// <summary>Тип контракта.</summary>
    public required ContractType Type { get; init; }

    /// <summary>Код поставляемого материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>Объём поставки.</summary>
    public required decimal Volume { get; init; }

    /// <summary>Цена за единицу.</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>Ставка штрафа за срыв поставки.</summary>
    public required decimal PenaltyRate { get; init; }

    /// <summary>Ход вступления в силу — на этом этапе всегда заглушка, см. <see cref="ContractTerms.EffectiveTurn"/>.</summary>
    public required int EffectiveTurn { get; init; }

    /// <summary>Ход поставки — только для spot.</summary>
    public required int? SpotDeliveryTurn { get; init; }

    /// <summary>Последний ход действия — только для срочного recurring.</summary>
    public required int? RecurringEndTurn { get; init; }

    public override void Apply(GameSessionState state)
    {
        var material = state.Config.Materials[MaterialId];
        var terms = new ContractTerms(
            Type, material, Volume, UnitPrice, PenaltyRate, EffectiveTurn, SpotDeliveryTurn, RecurringEndTurn);
        var proposal = new ContractProposal(BuyerTeamId, SellerTeamId, SubmittedByTeamId, terms);

        state.AddContractProposal(new PendingContractProposal(ProposalId, proposal, state.CurrentTurn));
    }
}
