namespace Game.Config.Session;

/// <summary>
/// Стартовые условия команды (SPEC §5.1): фабрик нет, только деньги — кредит свободной суммы
/// под процент, растущий с размером займа. <see cref="BaseLoanInterestRate"/> и
/// <see cref="LoanInterestRateGrowthPerUnitBorrowed"/> — это общая кривая ставки по долгу команды
/// (SPEC §5.9), применяемая не только к стартовому займу, но и к любому последующему кредиту,
/// включая принудительный (последний дополнительно несёт <see cref="ForcedLoanPenaltyRatePerOccurrence"/>
/// — «ставка принудительного займа заведомо хуже любого добровольного»), и надбавку за репутацию
/// (<see cref="MaxReputationRatePenalty"/>, Блок 6.2). Все числа — заглушки, требуют калибровки.
/// </summary>
public sealed record StartingConditionsConfig
{
    /// <summary>Максимальная сумма стартового кредита, которую может выбрать команда.</summary>
    public required decimal MaxStartingLoanAmount { get; init; }

    /// <summary>Базовая процентная ставка по долгу команды (за ход).</summary>
    public required decimal BaseLoanInterestRate { get; init; }

    /// <summary>Прирост ставки за каждую единицу текущего долга сверх базовой (долг растёт в цене с размером).</summary>
    public required decimal LoanInterestRateGrowthPerUnitBorrowed { get; init; }

    /// <summary>
    /// Штрафная надбавка к ставке, добавляемая ко всему долгу команды за каждый случай
    /// принудительного кредита (накопительно — см. <see cref="Game.Domain.Team.PenaltyRateSurcharge"/>).
    /// </summary>
    public required decimal ForcedLoanPenaltyRatePerOccurrence { get; init; }

    /// <summary>
    /// Надбавка к ставке при нулевой публичной репутации команды (SPEC §5.9: «ставка зависит от
    /// закредитованности и репутации»); линейно убывает до 0 при 100% репутации. У команды без
    /// истории поставок репутация по умолчанию 100% — надбавка не действует до первого нарекания.
    /// </summary>
    public required decimal MaxReputationRatePenalty { get; init; }
}
