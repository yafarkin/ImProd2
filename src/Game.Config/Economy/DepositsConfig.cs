namespace Game.Config.Economy;

/// <summary>
/// Параметры банковских депозитов (SPEC §5.9) — кандидат к включению в MVP (SPEC §2), включаются
/// флагом в <see cref="FeatureFlagsConfig"/>. Число — заглушка, требует калибровки.
/// </summary>
public sealed record DepositsConfig
{
    /// <summary>Эталонная ставка доходности депозита, за ход.</summary>
    public required decimal InterestRatePerTurn { get; init; }
}
