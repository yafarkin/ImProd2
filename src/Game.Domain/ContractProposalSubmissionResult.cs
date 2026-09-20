namespace Game.Domain;

/// <summary>
/// Результат подачи одной заявки на сделку (SPEC §6, <c>docs/TODO.md</c> №16). Отличается от <see
/// cref="ContractFormationResult"/> тем, что несведённая заявка здесь — не отказ, а нормальный
/// промежуточный исход: она сохранена и ждёт встречной. <see cref="Mismatches"/> при этом либо
/// пуст (встречной заявки ещё нет вовсе), либо перечисляет разошедшиеся **поля** ближайшей
/// встречной заявки — без чужих значений, иначе сверка выродится в копирование условий.
/// </summary>
public sealed class ContractProposalSubmissionResult
{
    /// <summary>Заявки сошлись, контракт заключён.</summary>
    public bool IsMatched { get; }

    /// <summary>Идентификатор поданной заявки — заполнен всегда, заявка пишется в журнал в любом случае.</summary>
    public Ulid ProposalId { get; }

    /// <summary>Заключённый контракт — заполнен только при <see cref="IsMatched"/>.</summary>
    public Contract? Contract { get; }

    /// <summary>Разошедшиеся поля ближайшей встречной заявки; пусто, если встречной заявки нет.</summary>
    public IReadOnlyList<ContractMismatchReason> Mismatches { get; }

    /// <summary>Встречная заявка есть, но условия по ней не сошлись.</summary>
    public bool HasCounterpartyProposal => !IsMatched && Mismatches.Count > 0;

    private ContractProposalSubmissionResult(
        bool isMatched, Ulid proposalId, Contract? contract, IReadOnlyList<ContractMismatchReason> mismatches)
    {
        IsMatched = isMatched;
        ProposalId = proposalId;
        Contract = contract;
        Mismatches = mismatches;
    }

    /// <summary>Заявка сошлась со встречной — контракт создан.</summary>
    public static ContractProposalSubmissionResult Matched(Ulid proposalId, Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return new ContractProposalSubmissionResult(true, proposalId, contract, Array.Empty<ContractMismatchReason>());
    }

    /// <summary>Заявка сохранена и ждёт встречной.</summary>
    public static ContractProposalSubmissionResult Pending(
        Ulid proposalId, IReadOnlyList<ContractMismatchReason> mismatches)
    {
        ArgumentNullException.ThrowIfNull(mismatches);
        return new ContractProposalSubmissionResult(false, proposalId, null, mismatches);
    }
}
