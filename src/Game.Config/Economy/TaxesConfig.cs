namespace Game.Config.Economy;

/// <summary>
/// Параметры налогов (SPEC §5.10) — кандидат к включению в MVP (SPEC §2), включаются флагом
/// в <see cref="FeatureFlagsConfig"/>. Числа — заглушки, требуют калибровки.
/// </summary>
public sealed record TaxesConfig
{
    /// <summary>Налог на имущество: доля от стоимости фабрики, списываемая за ход.</summary>
    public required decimal PropertyTaxRatePerTurn { get; init; }

    /// <summary>Налог с продаж: доля от суммы сделки.</summary>
    public required decimal SalesTaxRate { get; init; }
}
