using Game.Engine;

namespace Game.Web;

/// <summary>
/// Форматирование фазы/отсчёта/последней новости для страниц-заглушек Команды и Оператора (Блок
/// 8.2) — общая логика вынесена сюда, чтобы обе страницы не дублировали один и тот же скан
/// <see cref="GameSession.Entries"/> и одно и то же сопоставление фаз русским подписям.
/// </summary>
public static class PhaseDisplay
{
    /// <summary>Русская подпись фазы для экрана.</summary>
    public static string PhaseLabel(TurnPhase phase) => phase switch
    {
        TurnPhase.Settlement => "Расчёт",
        TurnPhase.Decision => "Решения",
        _ => phase.ToString()
    };

    /// <summary>Короткое пояснение смысла фазы для игроков (SPEC §4) — что сейчас происходит и можно ли действовать.</summary>
    public static string PhaseExplanation(TurnPhase phase) => phase switch
    {
        TurnPhase.Settlement => "Система уже посчитала итоги хода — рынок, производство, финансы. Просмотрите результаты и дождитесь начала решений, действия команд пока не принимаются.",
        TurnPhase.Decision => "Ваше окно для действий: стройте и прокачивайте фабрики, нанимайте рабочих, ведите переговоры и заключайте сделки.",
        _ => string.Empty
    };

    /// <summary>Остаток фазы как «мм:сс»; отрицательный/просроченный остаток показывается как «00:00».</summary>
    public static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        return $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
    }

    /// <summary>Текст последнего опубликованного заголовка новости или null, если новостей ещё не было.</summary>
    public static string? FindLastNewsHeadline(GameSession session)
    {
        for (var i = session.Entries.Count - 1; i >= 0; i--)
        {
            if (session.Entries[i].Change is NewsPublished newsPublished)
            {
                return newsPublished.Headline;
            }
        }

        return null;
    }

    /// <summary>Тексты последних опубликованных заголовков новостей, самый новый первым — для бегущей строки большого экрана (Блок 9.7).</summary>
    public static IReadOnlyList<string> RecentNewsHeadlines(GameSession session, int count)
    {
        var result = new List<string>();
        for (var i = session.Entries.Count - 1; i >= 0 && result.Count < count; i--)
        {
            if (session.Entries[i].Change is NewsPublished newsPublished)
            {
                result.Add(newsPublished.Headline);
            }
        }

        return result;
    }
}
