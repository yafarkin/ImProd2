namespace Game.Config.Session;

/// <summary>
/// Стартовые условия команды (SPEC §5.1): фабрик нет, только деньги — кредит свободной суммы
/// под процент, растущий с размером займа. <see cref="BaseLoanInterestRate"/> и
/// <see cref="LoanInterestRateGrowthPerUnitBorrowed"/> — это общая кривая ставки по долгу команды
/// (SPEC §5.9), применяемая не только к стартовому займу, но и к любому последующему кредиту,
/// включая принудительный (последний дополнительно несёт <see cref="ForcedLoanPenaltyRatePerOccurrence"/>
/// — «ставка принудительного займа заведомо хуже любого добровольного»). Все числа — заглушки,
/// требуют калибровки.
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
}
