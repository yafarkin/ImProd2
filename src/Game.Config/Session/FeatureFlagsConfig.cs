namespace Game.Config.Session;

/// <summary>Флаги MVP (SPEC §2): механики, которые можно включать/выключать без перекомпиляции.</summary>
public sealed record FeatureFlagsConfig
{
    /// <summary>Включены ли налоги (SPEC §5.10).</summary>
    public required bool TaxesEnabled { get; init; }

    /// <summary>Включены ли банковские депозиты (SPEC §5.9).</summary>
    public required bool DepositsEnabled { get; init; }

    /// <summary>Включена ли аварийная закупка у системы по умолчанию (SPEC §5.3; на пилоте — включена).</summary>
    public required bool EmergencyPurchaseEnabled { get; init; }
}
