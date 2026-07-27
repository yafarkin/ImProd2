namespace Game.Engine;

/// <summary>
/// Ведущий включил/выключил аварийную закупку на время сессии (Блок 9.6, SPEC §9.5) — поверх
/// стартового значения из <c>FeatureFlagsConfig</c>, заданного при старте сессии
/// (<see cref="SessionStarted"/>).
/// </summary>
public sealed record EmergencyPurchaseToggled : Change<GameSessionState>
{
    /// <summary>Новое значение флага.</summary>
    public required bool Enabled { get; init; }

    public override void Apply(GameSessionState state) => state.EmergencyPurchaseEnabled = Enabled;
}
