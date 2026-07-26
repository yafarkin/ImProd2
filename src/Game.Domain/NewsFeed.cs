namespace Game.Domain;

/// <summary>
/// Публикация новостной ленты сессии (Блок 6.3, SPEC §5.4, §13): помнит, какие заголовки уже
/// прозвучали в этой сессии, чтобы подбор следующего не повторялся — ни автоматический по тренду,
/// ни ручное событие ведущего (оба используют один и тот же пул).
/// </summary>
public sealed class NewsFeed
{
    private readonly HashSet<string> _publishedItemIds = new();

    /// <summary>Публиковался ли уже в этой сессии заголовок с данным кодом.</summary>
    public bool IsPublished(string newsItemId) => _publishedItemIds.Contains(newsItemId);

    /// <summary>Отмечает заголовок как опубликованный.</summary>
    public void Record(string newsItemId) => _publishedItemIds.Add(newsItemId);
}
