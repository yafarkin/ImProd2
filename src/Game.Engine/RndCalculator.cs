using Game.Config.Economy;

namespace Game.Engine;

/// <summary>Чистый расчёт того, на какой уровень должна выйти фабрика при данных накопленных R&amp;D-вложениях (SPEC §5.8).</summary>
public static class RndCalculator
{
    /// <summary>
    /// Уровень, соответствующий накопленным вложениям: поднимается, пока накопленное покрывает
    /// очередной порог из <see cref="RndConfig.CumulativeInvestmentThresholdsByLevel"/>. Вложение,
    /// сразу перекрывающее несколько порогов, поднимает уровень на несколько ступеней за раз.
    /// </summary>
    public static int CalculateResultingLevel(int currentLevel, decimal cumulativeInvestment, RndConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var level = currentLevel;
        while (level - 1 < config.CumulativeInvestmentThresholdsByLevel.Count
               && cumulativeInvestment >= config.CumulativeInvestmentThresholdsByLevel[level - 1])
        {
            level++;
        }

        return level;
    }
}
