using Game.Config.Economy;

namespace Game.Engine;

/// <summary>Чистый расчёт того, на какой уровень должна выйти фабрика при данных накопленных R&amp;D-вложениях (SPEC §5.8).</summary>
public static class RndCalculator
{
    /// <summary>
    /// Пересчитывает накопленные ¤, вложенные в фабрику, в очки исследований по вогнутой кривой (см.
    /// <see cref="RndConfig.DiminishingReturnsExponent"/>) — тот же приём и по той же причине, что и
    /// <see cref="GenerationResearchCalculator.CalculateResearchPoints"/>: разовое крупное вложение
    /// даёт меньше суммарной отдачи, чем то же вложение, растянутое по ходам.
    /// </summary>
    public static decimal CalculateResearchPoints(decimal cumulativeInvestment, RndConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return cumulativeInvestment <= 0
            ? 0m
            : (decimal)Math.Pow((double)cumulativeInvestment, (double)config.DiminishingReturnsExponent);
    }

    /// <summary>
    /// Уровень, соответствующий накопленным вложениям: поднимается, пока пересчитанные в очки
    /// вложения покрывают очередной порог из <see cref="RndConfig.ResearchPointThresholdsByLevel"/>.
    /// Вложение, сразу перекрывающее несколько порогов, поднимает уровень на несколько ступеней за раз.
    /// </summary>
    public static int CalculateResultingLevel(int currentLevel, decimal cumulativeInvestment, RndConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var points = CalculateResearchPoints(cumulativeInvestment, config);
        var level = currentLevel;
        while (level - 1 < config.ResearchPointThresholdsByLevel.Count
               && points >= config.ResearchPointThresholdsByLevel[level - 1])
        {
            level++;
        }

        return level;
    }

    /// <summary>
    /// Достигнут ли потолок уровней из <see cref="RndConfig.ResearchPointThresholdsByLevel"/> — выше
    /// уже некуда, дальнейшие вложения ничего не меняют (баг-репорт пользователя: если продолжать
    /// списывать деньги на этом этапе, они уходят впустую — см. <see cref="RndInvestmentStep"/>, а
    /// также используется UI, чтобы прятать форму вложения, когда вкладывать больше некуда).
    /// </summary>
    public static bool IsAtMaxLevel(int currentLevel, RndConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return currentLevel - 1 >= config.ResearchPointThresholdsByLevel.Count;
    }
}
