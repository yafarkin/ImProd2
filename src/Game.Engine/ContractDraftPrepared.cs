using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Переговорщик передал управляющему своей команды черновик сделки (SPEC §3). Это решение
/// переговорщика, а не заявка на сделку: контрагент о черновике не узнаёт, на сверку уходит только
/// заявка, которую управляющий по нему подаст (<see cref="ContractProposalSubmitted.SourceDraftId"/>).
/// </summary>
public sealed record ContractDraftPrepared : Change<GameSessionState>
{
    /// <summary>Идентификатор черновика.</summary>
    public required Ulid DraftId { get; init; }

    /// <summary>Код входа переговорщика, передавшего черновик.</summary>
    public required string PreparedByParticipantCode { get; init; }

    /// <summary>Команда-покупатель.</summary>
    public required Ulid BuyerTeamId { get; init; }

    /// <summary>Команда-продавец.</summary>
    public required Ulid SellerTeamId { get; init; }

    /// <summary>Команда переговорщика — от её имени будет подана заявка.</summary>
    public required Ulid TeamId { get; init; }

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

    /// <summary>Ход вступления в силу — заглушка, как и у заявки, см. <see cref="ContractTerms.EffectiveTurn"/>.</summary>
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
        var proposal = new ContractProposal(BuyerTeamId, SellerTeamId, TeamId, terms);

        state.AddContractDraft(new ContractDraft(DraftId, proposal, PreparedByParticipantCode, state.CurrentTurn));
    }
}
