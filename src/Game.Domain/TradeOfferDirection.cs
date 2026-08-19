namespace Game.Domain;

/// <summary>Роль команды, опубликовавшей запись на доске публичных заявок (см. <see cref="TradeOffer"/>).</summary>
public enum TradeOfferDirection
{
    /// <summary>Команда предлагает материал на продажу.</summary>
    Sell,

    /// <summary>Команда ищет материал на покупку.</summary>
    Buy,
}
