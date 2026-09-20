namespace Game.Config.Economy;

/// <summary>
/// Параметры R&amp;D (SPEC §5.8): накопительные вложения в фабрику поднимают её уровень, уровень —
/// множитель к скорости производства. Отдача от вложенных денег — намеренно нелинейная (см.
/// <see cref="DiminishingReturnsExponent"/>), тот же приём, что и у командного исследования
/// поколения (<see cref="GenerationResearchConfig"/>) — запрос пользователя: обе прогрессии должны
/// говорить на одном языке «очков исследования», а не одна в деньгах, другая в очках. Числа —
/// заглушки, требуют калибровки.
/// </summary>
public sealed record RndConfig
{
    /// <summary>
    /// Очки исследований (не сырые ¤, см. <see cref="DiminishingReturnsExponent"/>), нужные для
    /// перехода фабрики с уровня (индекс i + 1) на (индекс i + 2); индекс 0 — с уровня 1 на 2, индекс
    /// 1 — с 2 на 3, и т.д. Вложения не сбрасываются между переходами (SPEC §5.8: «накопительные по
    /// ходам»). Вложение, сразу перекрывающее несколько порогов, поднимает уровень на несколько
    /// ступеней за раз — тот же приём, что и у <see
    /// cref="GenerationResearchConfig.ResearchPointThresholdsByGeneration"/>.
    /// </summary>
    public required IReadOnlyList<decimal> ResearchPointThresholdsByLevel { get; init; }

    /// <summary>
    /// Показатель степени p (0..1) пересчёта накопленных ¤, вложенных в фабрику, в очки исследований:
    /// очки = (накопленные ¤)^p — тот же приём, что и <see
    /// cref="GenerationResearchConfig.DiminishingReturnsExponent"/>. Чем меньше p, тем сильнее
    /// выражена убывающая отдача от разовых крупных вложений.
    /// </summary>
    public required decimal DiminishingReturnsExponent { get; init; }

    /// <summary>
    /// Прирост скорости производства фабрики (<c>Recipe.ProductionRate</c>) за каждый уровень сверх
    /// первого — например, 0.1 означает +10% за уровень.
    /// </summary>
    public required decimal ProductionRateBonusPerLevel { get; init; }

    /// <summary>
    /// Потолок суммы, которую команда может выделить на R&amp;D одной фабрики за ход (запрос
    /// пользователя: нельзя мгновенно прокачать фабрику на несколько уровней за один ход, даже при
    /// сколь угодно большом балансе — прогресс должен быть растянут по ходам). Действует на одну фабрику, не
    /// на команду суммарно — R&amp;D и так привязан к конкретной фабрике (см. doc-comment класса).
    /// Заглушка, требует калибровки.
    /// </summary>
    public required decimal MaxCommitmentPerTurn { get; init; }
}
