using Game.Domain;

namespace Game.Bots;

/// <summary>
/// Заявка бота в упрощённый биржевой стакан (Блок 7.3.1, <c>docs/balancing-bots.md</c> §1) — не
/// переговоры, а механический способ найти контрагента и цену: <see cref="SimpleBot.ComputeSellOrders"/>
/// и <see cref="SimpleBot.ComputeBuyOrders"/> формируют заявки, <see cref="OrderBook.Match"/> сводит
/// их в контракты. Заявка живёт один ход решений — непокрытый остаток не переносится на следующий
/// ход, а пересчитывается заново (самый простой вариант из открытых в доке, решённый на старте блока).
/// </summary>
public sealed record TradeOrder
{
    /// <summary>Команда, подавшая заявку.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Материал заявки.</summary>
    public required Material Material { get; init; }

    /// <summary>Желаемый объём (положительный).</summary>
    public required decimal Volume { get; init; }

    /// <summary>
    /// Предельная цена: для заявки на продажу — минимально приемлемая, для заявки на покупку —
    /// максимально приемлемая. Сама сделка, если состоится, идёт по текущей рыночной котировке (см.
    /// <see cref="OrderBook.Match"/>), эта цена — только фильтр «готов ли контрагент вообще торговать
    /// по рыночной цене сейчас».
    /// </summary>
    public required decimal LimitPrice { get; init; }
}
