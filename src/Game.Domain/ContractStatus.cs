namespace Game.Domain;

/// <summary>
/// Статус контракта (SPEC §6). Условия контракта неизменяемы после подписания — меняется только
/// статус.
/// </summary>
public enum ContractStatus
{
    /// <summary>Условия обеих сторон совпали, код подтверждения выдан, ждём подтверждения управляющим.</summary>
    PendingConfirmation,

    /// <summary>Контракт подтверждён и действует.</summary>
    Active,

    /// <summary>Контракт прекращён целиком (SPEC §6: mutual/voluntary — расчёт штрафа и репутации делает Блок 5.2).</summary>
    Terminated
}
