namespace Game.Config;

/// <summary>
/// Стартовые условия команды (SPEC §5.1): фабрик нет, только деньги — кредит свободной суммы
/// под процент, растущий с размером займа. Все числа — заглушки, требуют калибровки.
/// </summary>
public sealed record StartingConditionsConfig
{
    /// <summary>Максимальная сумма стартового кредита, которую может выбрать команда.</summary>
    public required decimal MaxStartingLoanAmount { get; init; }

    /// <summary>Базовая процентная ставка стартового кредита (за ход).</summary>
    public required decimal BaseLoanInterestRate { get; init; }

    /// <summary>Прирост ставки за каждую единицу суммы займа сверх базовой (кредит растёт в цене с размером).</summary>
    public required decimal LoanInterestRateGrowthPerUnitBorrowed { get; init; }
}
