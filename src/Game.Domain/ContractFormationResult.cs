namespace Game.Domain;

/// <summary>Результат попытки свести две независимо поданные заявки в контракт (SPEC §6).</summary>
public sealed class ContractFormationResult
{
    /// <summary>Заявки сошлись, и контракт создан.</summary>
    public bool IsMatched { get; }

    /// <summary>Новый контракт в статусе <see cref="ContractStatus.PendingConfirmation"/> — заполнен только при <see cref="IsMatched"/>.</summary>
    public Contract? Contract { get; }

    /// <summary>Что именно разошлось — заполнено только когда <see cref="IsMatched"/> равно false.</summary>
    public IReadOnlyList<ContractMismatchReason> Mismatches { get; }

    private ContractFormationResult(bool isMatched, Contract? contract, IReadOnlyList<ContractMismatchReason> mismatches)
    {
        IsMatched = isMatched;
        Contract = contract;
        Mismatches = mismatches;
    }

    public static ContractFormationResult Matched(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return new ContractFormationResult(true, contract, Array.Empty<ContractMismatchReason>());
    }

    public static ContractFormationResult Conflict(IReadOnlyList<ContractMismatchReason> mismatches)
    {
        ArgumentNullException.ThrowIfNull(mismatches);
        if (mismatches.Count == 0)
        {
            throw new ArgumentException("A conflict result must list at least one mismatch.", nameof(mismatches));
        }

        return new ContractFormationResult(false, null, mismatches);
    }
}
