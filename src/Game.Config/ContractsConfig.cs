namespace Game.Config;

/// <summary>
/// Параметры контрактов (SPEC §6): штрафы за два уровня несоблюдения (Delivery Miss ниже
/// Termination), барьер для одностороннего расторжения. Числа — заглушки, требуют калибровки.
/// </summary>
public sealed record ContractsConfig
{
    /// <summary>Ставка штрафа за пропуск отдельной поставки (Delivery Miss), доля от суммы поставки.</summary>
    public required decimal DeliveryMissPenaltyRate { get; init; }

    /// <summary>Ставка штрафа за прекращение контракта целиком (Termination); существенно выше Delivery Miss.</summary>
    public required decimal TerminationPenaltyRate { get; init; }

    /// <summary>Фиксированная плата за одностороннее (voluntary) расторжение — намеренно высокий барьер.</summary>
    public required decimal VoluntaryTerminationFee { get; init; }

    /// <summary>Лимит активных контрактов на команду; null — без лимита. Открытый вопрос SPEC §16.</summary>
    public required int? MaxActiveContractsPerTeam { get; init; }
}
