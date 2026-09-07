namespace Game.Domain;

/// <summary>
/// Публикация новостной ленты сессии (Блок 6.3, SPEC §5.4, §13): помнит, какие заголовки и на каком
/// ходу уже прозвучали в этой сессии, чтобы подбор следующего сначала брал ещё не звучавшие, а когда
/// пул тренда исчерпан — самый давний из уже звучавших (блок 11.9, <c>docs/external-economy.md</c> §5).
/// Пул общий для автоматического подбора по тренду и для ручного события ведущего.
/// </summary>
public sealed class NewsFeed
{
    private readonly Dictionary<string, int> _lastPublishedTurn = new();

    /// <summary>Код заголовка, прозвучавшего последним; null, если лента ещё пуста.</summary>
    public string? LastPublishedItemId { get; private set; }

    /// <summary>Публиковался ли уже в этой сессии заголовок с данным кодом.</summary>
    public bool IsPublished(string newsItemId) => _lastPublishedTurn.ContainsKey(newsItemId);

    /// <summary>
    /// Ход последней публикации заголовка; null, если он ещё не звучал. Это и есть ключ сортировки
    /// «давно не звучавших» при исчерпании пула.
    /// </summary>
    public int? LastPublishedTurn(string newsItemId) =>
        _lastPublishedTurn.TryGetValue(newsItemId, out var turn) ? turn : null;

    /// <summary>Отмечает заголовок как опубликованный на ходу <paramref name="turn"/>.</summary>
    public void Record(string newsItemId, int turn)
    {
        _lastPublishedTurn[newsItemId] = turn;
        LastPublishedItemId = newsItemId;
    }
}
