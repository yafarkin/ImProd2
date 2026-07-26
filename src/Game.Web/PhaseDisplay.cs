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
        TurnPhase.Calculation => "Расчёт",
        TurnPhase.Decision => "Решения",
        TurnPhase.Closing => "Завершение",
        _ => phase.ToString()
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
}
