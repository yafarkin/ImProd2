using Game.Domain;

namespace Game.Engine;

/// <summary>Команда опубликовала заявку на доске публичных заявок (запрос пользователя 2026-08-17).</summary>
public sealed record TradeOfferPosted : Change<GameSessionState>
{
    /// <summary>Команда, опубликовавшая заявку.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Идентификатор заявки.</summary>
    public required Ulid TradeOfferId { get; init; }

    /// <summary>Продаёт или покупает публикующая команда.</summary>
    public required TradeOfferDirection Direction { get; init; }

    /// <summary>Код материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>Разовая поставка или регулярные поставки.</summary>
    public required ContractType Type { get; init; }

    /// <summary>Объём за одну поставку.</summary>
    public required decimal Volume { get; init; }

    /// <summary>Минимально приемлемая цена за единицу.</summary>
    public required decimal MinPrice { get; init; }

    /// <summary>Максимально приемлемая цена за единицу.</summary>
    public required decimal MaxPrice { get; init; }

    public override void Apply(GameSessionState state)
    {
        var material = state.Config.Materials[MaterialId];
        state.AddTradeOffer(new TradeOffer(TradeOfferId, TeamId, Direction, material, Type, Volume, MinPrice, MaxPrice, state.CurrentTurn));
    }
}
