using Game.Config.Economy;

namespace Game.Engine;

/// <summary>
/// Новостная лента получила новый заголовок (Блок 6.3, SPEC §4 — «новости/тренды» идёт после
/// обновления рынка; SPEC §5.4, §13). Одно и то же событие обслуживает и автоматический подбор по
/// тренду (<see cref="NewsCalculator.SelectNext"/> из <see cref="GameSession.RunTick"/>), и ручное
/// событие ведущего (<see cref="GameSession.PublishManualNews"/>) — оба делят один пул уже
/// прозвучавших заголовков. Несёт готовый текст, а не только код заголовка, — экраны читают его
/// прямо из события, не заглядывая в GameConfig (AGENTS-память о трассируемости причин).
/// </summary>
public sealed record NewsPublished : Change<GameSessionState>
{
    /// <summary>Ход, на котором опубликован заголовок.</summary>
    public required int Turn { get; init; }

    /// <summary>Код заголовка в библиотеке (<c>NewsItemConfig.Id</c>) — для защиты от повтора.</summary>
    public required string NewsItemId { get; init; }

    /// <summary>Тренд, которому соответствует заголовок.</summary>
    public required EconomyTrend Trend { get; init; }

    /// <summary>Текст заголовка.</summary>
    public required string Headline { get; init; }

    public override void Apply(GameSessionState state) => state.NewsFeed.Record(NewsItemId);
}
