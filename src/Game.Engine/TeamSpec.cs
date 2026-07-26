namespace Game.Engine;

/// <summary>
/// Состав одной команды на момент старта сессии — часть содержимого <see cref="SessionStarted"/>.
/// Регистрация игроков (SPEC §9.6) происходит до старта таймера, поэтому ростер уже известен
/// целиком к моменту, когда ведущий запускает сессию.
/// </summary>
public sealed record TeamSpec
{
    /// <summary>Идентификатор команды.</summary>
    public required Ulid Id { get; init; }

    /// <summary>Отображаемое имя команды.</summary>
    public required string Name { get; init; }

    /// <summary>Код сектора команды (<see cref="Game.Config.Catalog.SectorConfig.Id"/>).</summary>
    public required string SectorId { get; init; }

    /// <summary>Сумма стартового кредита, выбранная командой при регистрации (SPEC §5.1); 0, если не бралась.</summary>
    public required decimal StartingLoanAmount { get; init; }
}
