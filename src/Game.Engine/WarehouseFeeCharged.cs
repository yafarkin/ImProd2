namespace Game.Engine;

/// <summary>
/// Списана плата за превышение бесплатного лимита склада команды за ход (SPEC §5.7) — считается по
/// суммарному остатку по всем материалам, а не по отдельным видам (<see cref="WarehouseFeeCalculator"/>).
/// </summary>
public sealed record WarehouseFeeCharged : Change<GameSessionState>
{
    /// <summary>Команда, с которой списана плата.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Объём склада сверх бесплатного лимита, за который начислена плата — для аудита.</summary>
    public required decimal OverageQuantity { get; init; }

    /// <summary>Списанная сумма.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state) => state.Teams[TeamId].Debit(Amount);
}
