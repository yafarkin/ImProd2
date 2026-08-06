namespace Game.Config.Economy;

/// <summary>
/// Командное (не пофабричное) исследование, разблокирующее доступ к постройке более глубоких
/// переделов пирамиды сырья (пользовательский запрос: фабрики будущих поколений не должны быть
/// доступны с хода 1). Отдача от вложенных денег — намеренно нелинейная (см.
/// <see cref="DiminishingReturnsExponent"/>), считается от НАКОПЛЕННОЙ с начала игры суммы, а не от
/// суммы за конкретный ход — иначе дробление одной и той же суммы на много ходов давало бы больше
/// суммарного прогресса, чем один платёж, обратный эффект тому, что нужно. Все числа — заглушки,
/// требуют калибровки.
/// </summary>
public sealed record GenerationResearchConfig
{
    /// <summary>Поколение (см. <see cref="Game.Domain.Material.Level"/>), с которого команда начинает без всякого исследования — обычно 1.</summary>
    public required int StartingGeneration { get; init; }

    /// <summary>
    /// Очки исследований (не сырые ¤, см. <see cref="DiminishingReturnsExponent"/>), нужные для
    /// перехода с поколения (<see cref="StartingGeneration"/> + i) на (<see cref="StartingGeneration"/> + i + 1);
    /// индекс i = 0, 1, 2... Вложение, сразу перекрывающее несколько порогов, поднимает поколение на
    /// несколько ступеней за раз — тот же приём, что и у <see cref="RndConfig.CumulativeInvestmentThresholdsByLevel"/>.
    /// </summary>
    public required IReadOnlyList<decimal> ResearchPointThresholdsByGeneration { get; init; }

    /// <summary>
    /// Показатель степени p (0..1) пересчёта накопленных ¤ в очки исследований: очки = (накопленные ¤)^p.
    /// Чем меньше p, тем сильнее выражена убывающая отдача от разовых крупных вложений — вложить в 10
    /// раз больше денег даёт заметно меньше чем в 10 раз больше очков.
    /// </summary>
    public required decimal DiminishingReturnsExponent { get; init; }

    /// <summary>Потолок объявляемой суммы на одну команду за ход — тот же приём, что и у <see cref="RndConfig.MaxCommitmentPerTurn"/>.</summary>
    public required decimal MaxCommitmentPerTurn { get; init; }
}
