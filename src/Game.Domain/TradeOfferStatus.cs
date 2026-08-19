namespace Game.Domain;

/// <summary>Текущий статус записи на доске публичных заявок (см. <see cref="TradeOffer"/>).</summary>
public enum TradeOfferStatus
{
    /// <summary>Заявка открыта, ждёт исполнения.</summary>
    Open,

    /// <summary>Заявку кто-то исполнил — по ней уже заключён контракт.</summary>
    Fulfilled,

    /// <summary>Команда сама отозвала заявку.</summary>
    Withdrawn,
}
