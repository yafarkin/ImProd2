namespace Game.Engine;

/// <summary>
/// Заявка на доске публичных заявок исполнена другой командой (запрос пользователя 2026-08-17) —
/// только фиксирует судьбу самой заявки; сам контракт и его подтверждение — отдельные события
/// (<see cref="ContractSigned"/>/<see cref="ContractConfirmed"/>), которые вызывающий код
/// (<see cref="GameSession.FulfillTradeOffer"/>) записывает раньше этого, тем же приёмом, что и
/// <c>Game.Bots.OrderBook.SignContract</c> для механического стакана SimpleBot.
/// </summary>
public sealed record TradeOfferFulfilled : Change<GameSessionState>
{
    /// <summary>Исполненная заявка.</summary>
    public required Ulid TradeOfferId { get; init; }

    /// <summary>Команда, исполнившая заявку.</summary>
    public required Ulid FulfillingTeamId { get; init; }

    public override void Apply(GameSessionState state) => state.TradeOffers[TradeOfferId].Fulfill();
}
