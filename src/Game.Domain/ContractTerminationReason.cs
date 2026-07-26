namespace Game.Domain;

/// <summary>
/// Причина прекращения контракта целиком (SPEC §6). Не включает срыв отдельной поставки (Delivery
/// Miss) — тот не прекращает контракт, см. <see cref="ContractStatus"/>.
/// </summary>
public enum ContractTerminationReason
{
    /// <summary>Обоюдное расторжение — без штрафов.</summary>
    Mutual,

    /// <summary>Одностороннее расторжение — дорогое, намеренно высокий барьер.</summary>
    Voluntary
}
