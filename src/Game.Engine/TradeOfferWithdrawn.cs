namespace Game.Engine;

/// <summary>Команда отозвала свою заявку с доски публичных заявок (запрос пользователя 2026-08-17).</summary>
public sealed record TradeOfferWithdrawn : Change<GameSessionState>
{
    /// <summary>Отзываемая заявка.</summary>
    public required Ulid TradeOfferId { get; init; }

    public override void Apply(GameSessionState state) => state.TradeOffers[TradeOfferId].Withdraw();
}
