namespace Game.Engine;

/// <summary>
/// Есть ли сейчас висящее предложение пересмотра условий контракта (Блок 9.3, SPEC §6) — по образцу
/// <see cref="PhaseTimerCalculator"/>: сканирует журнал с конца, а не хранит отдельное поле в
/// состоянии. Нужен из двух мест (валидация команд и чтение для UI), поэтому вынесен в отдельный
/// статический класс, а не приватный метод <see cref="GameSession"/>.
/// </summary>
public static class ContractRevisionCalculator
{
    /// <summary>
    /// Последнее предложение пересмотра для контракта, если оно ещё не разрешено (ни принято, ни
    /// отклонено) и контракт с тех пор не расторгнут; иначе <c>null</c>.
    /// </summary>
    public static ContractRevisionProposed? FindPending(
        IReadOnlyList<EventLogEntry<GameSessionState>> entries, Ulid contractId)
    {
        ArgumentNullException.ThrowIfNull(entries);

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            switch (entries[i].Change)
            {
                case ContractRevisionResolved resolved when resolved.ContractId == contractId:
                    return null;
                case ContractTerminated terminated when terminated.ContractId == contractId:
                    return null;
                case ContractRevisionProposed proposed when proposed.ContractId == contractId:
                    return proposed;
            }
        }

        return null;
    }
}
