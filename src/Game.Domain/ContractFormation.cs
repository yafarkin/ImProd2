namespace Game.Domain;

/// <summary>
/// Сведение двух независимо поданных заявок в контракт (SPEC §6): «обе команды вводят условия
/// независимо → система сверяет совпадение → генерирует код подтверждения». Несовпадение не
/// создаёт контракт вовсе — это обратная связь сторонам, а не сущность со своим жизненным циклом.
/// </summary>
public static class ContractFormation
{
    /// <summary>
    /// Сверяет две заявки. Совпадают, только если обе стороны сделки одинаковы, заявки поданы
    /// разными командами и условия идентичны — тогда создаётся новый контракт со свежим кодом
    /// подтверждения в статусе <see cref="ContractStatus.PendingConfirmation"/>. Иначе — конфликт
    /// со списком того, что именно разошлось. <paramref name="proposalA"/> считается инициатором
    /// (<see cref="Contract.ProposedByTeamId"/>) — именно её команда не сможет дать финальное
    /// подтверждение сама себе, см. <see cref="Contract.Confirm"/>.
    /// </summary>
    public static ContractFormationResult TryMatch(
        ContractProposal proposalA, ContractProposal proposalB, Ulid contractId, Random random)
    {
        ArgumentNullException.ThrowIfNull(proposalA);
        ArgumentNullException.ThrowIfNull(proposalB);

        var mismatches = new List<ContractMismatchReason>();

        if (proposalA.BuyerTeamId != proposalB.BuyerTeamId || proposalA.SellerTeamId != proposalB.SellerTeamId)
        {
            mismatches.Add(ContractMismatchReason.CounterpartiesDiffer);
        }
        if (proposalA.SubmittedByTeamId == proposalB.SubmittedByTeamId)
        {
            mismatches.Add(ContractMismatchReason.SubmittedByTheSameTeam);
        }
        if (proposalA.Terms != proposalB.Terms)
        {
            mismatches.Add(ContractMismatchReason.TermsDiffer);
        }

        if (mismatches.Count > 0)
        {
            return ContractFormationResult.Conflict(mismatches);
        }

        var code = ContractConfirmationCode.Generate(random);
        var contract = new Contract(
            contractId, proposalA.BuyerTeamId, proposalA.SellerTeamId, proposalA.Terms, code,
            proposedByTeamId: proposalA.SubmittedByTeamId);

        return ContractFormationResult.Matched(contract);
    }
}
