using Game.Config.Economy;

namespace Game.Config.News;

/// <summary>
/// Один заголовок новостной ленты (SPEC §13): привязан к тренду, выбирается без повторов
/// в пределах сессии.
/// </summary>
public sealed record NewsItemConfig
{
    /// <summary>Уникальный код заголовка.</summary>
    public required string Id { get; init; }

    /// <summary>Тренд, к которому относится заголовок.</summary>
    public required EconomyTrend Trend { get; init; }

    /// <summary>Текст заголовка.</summary>
    public required string Headline { get; init; }
}
