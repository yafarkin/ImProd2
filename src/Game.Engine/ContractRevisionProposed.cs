namespace Game.Engine;

/// <summary>
/// Команда предложила пересмотреть условия действующего recurring-контракта (Блок 9.3, SPEC §6):
/// вторая сторона вправе принять или отклонить, отказ не наказывается. Само событие не меняет
/// состояние — «есть ли сейчас висящее предложение» вычисляется сканированием журнала
/// (<see cref="ContractRevisionCalculator"/>), тем же приёмом, что репутация и таймер фазы, а не
/// отдельным хранимым полем на <see cref="Contract"/>.
/// </summary>
public sealed record ContractRevisionProposed : Change<GameSessionState>
{
    /// <summary>Пересматриваемый контракт.</summary>
    public required Ulid ContractId { get; init; }

    /// <summary>Команда, предложившая пересмотр.</summary>
    public required Ulid ProposingTeamId { get; init; }

    /// <summary>Предложенный новый объём поставки.</summary>
    public required decimal Volume { get; init; }

    /// <summary>Предложенная новая цена за единицу.</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>Предложенная новая ставка штрафа за срыв.</summary>
    public required decimal PenaltyRate { get; init; }

    /// <summary>Предложенный новый последний ход действия контракта.</summary>
    public required int RecurringEndTurn { get; init; }

    public override void Apply(GameSessionState state)
    {
    }
}
