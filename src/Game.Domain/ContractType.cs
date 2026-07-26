namespace Game.Domain;

/// <summary>Тип контракта (SPEC §6).</summary>
public enum ContractType
{
    /// <summary>Разовая поставка на конкретный ход.</summary>
    Spot,

    /// <summary>Регулярные поставки в течение диапазона ходов.</summary>
    Recurring
}
