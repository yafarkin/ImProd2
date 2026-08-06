using Game.Config.Economy;

namespace Game.Engine;

/// <summary>
/// Чистый расчёт того, на какое поколение пирамиды сырья должна выйти команда при данных
/// накопленных вложениях в исследование (Блок 9.2, пользовательский запрос: будущие фабрики
/// открываются постепенно, а не сразу целиком).
/// </summary>
public static class GenerationResearchCalculator
{
    /// <summary>
    /// Пересчитывает накопленные ¤ в очки исследований по вогнутой кривой (см.
    /// <see cref="GenerationResearchConfig.DiminishingReturnsExponent"/>) — намеренно нелинейно, чтобы
    /// разовое крупное вложение давало меньше суммарной отдачи, чем то же вложение, растянутое по ходам.
    /// </summary>
    public static decimal CalculateResearchPoints(decimal cumulativeInvestment, GenerationResearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return cumulativeInvestment <= 0
            ? 0m
            : (decimal)Math.Pow((double)cumulativeInvestment, (double)config.DiminishingReturnsExponent);
    }

    /// <summary>
    /// Поколение, соответствующее накопленным вложениям: поднимается, пока пересчитанные в очки
    /// вложения покрывают очередной порог из <see cref="GenerationResearchConfig.ResearchPointThresholdsByGeneration"/>.
    /// Вложение, сразу перекрывающее несколько порогов, поднимает поколение на несколько ступеней за раз.
    /// </summary>
    public static int CalculateResultingGeneration(int currentGeneration, decimal cumulativeInvestment, GenerationResearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var points = CalculateResearchPoints(cumulativeInvestment, config);
        var generation = currentGeneration;
        while (generation - config.StartingGeneration < config.ResearchPointThresholdsByGeneration.Count
               && points >= config.ResearchPointThresholdsByGeneration[generation - config.StartingGeneration])
        {
            generation++;
        }

        return generation;
    }
}
