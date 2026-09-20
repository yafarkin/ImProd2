namespace Game.Config.Session;

/// <summary>Флаги MVP (SPEC §2): механики, которые можно включать/выключать без перекомпиляции.</summary>
public sealed record FeatureFlagsConfig
{
    /// <summary>Включена ли аварийная закупка у системы по умолчанию (SPEC §5.3; на пилоте — включена).</summary>
    public required bool EmergencyPurchaseEnabled { get; init; }
}
