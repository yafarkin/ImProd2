using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Сериализуемый снимок условий контракта для журнала (аналог <see cref="TeamSpec"/>): ссылается
/// на материал по коду, а не тащит доменный объект <see cref="Material"/> в каждое событие — при
/// применении материал разрешается в канонический экземпляр из каталога сессии.
/// </summary>
public sealed record ContractSpec
{
    /// <summary>Идентификатор контракта.</summary>
    public required Ulid ContractId { get; init; }

    /// <summary>Команда-покупатель.</summary>
    public required Ulid BuyerTeamId { get; init; }

    /// <summary>Команда-продавец.</summary>
    public required Ulid SellerTeamId { get; init; }

    /// <summary>Код подтверждения сделки.</summary>
    public required string ConfirmationCode { get; init; }

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

    /// <summary>Ход вступления в силу.</summary>
    public required int EffectiveTurn { get; init; }

    /// <summary>Ход поставки — только для spot.</summary>
    public required int? SpotDeliveryTurn { get; init; }

    /// <summary>Последний ход действия — только для recurring.</summary>
    public required int? RecurringEndTurn { get; init; }

    /// <summary>Снимок условий уже созданного контракта — для записи в журнал.</summary>
    public static ContractSpec From(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var terms = contract.Terms;

        return new ContractSpec
        {
            ContractId = contract.Id,
            BuyerTeamId = contract.BuyerTeamId,
            SellerTeamId = contract.SellerTeamId,
            ConfirmationCode = contract.ConfirmationCode,
            Type = terms.Type,
            MaterialId = terms.Material.Id,
            Volume = terms.Volume,
            UnitPrice = terms.UnitPrice,
            PenaltyRate = terms.PenaltyRate,
            EffectiveTurn = terms.EffectiveTurn,
            SpotDeliveryTurn = terms.SpotDeliveryTurn,
            RecurringEndTurn = terms.RecurringEndTurn,
        };
    }

    /// <summary>Восстанавливает доменный <see cref="Contract"/>, разрешая материал из каталога сессии.</summary>
    public Contract ToContract(GameSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var material = state.Config.Materials[MaterialId];
        var terms = new ContractTerms(
            Type, material, Volume, UnitPrice, PenaltyRate, EffectiveTurn, SpotDeliveryTurn, RecurringEndTurn);

        return new Contract(ContractId, BuyerTeamId, SellerTeamId, terms, ConfirmationCode);
    }
}
