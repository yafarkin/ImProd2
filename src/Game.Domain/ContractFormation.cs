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
        mismatches.AddRange(CompareTerms(proposalA.Terms, proposalB.Terms));

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

    /// <summary>
    /// Пофайловая сверка условий — экран конфликта (SPEC §9.3) подсвечивает именно разошедшееся
    /// поле, а не сообщает «условия не совпали» целиком. Ход вступления в силу здесь не сверяется
    /// сознательно: стороны его больше не заявляют, он подставляется при активации (SPEC §6,
    /// правка Блока 9.4, <see cref="Contract.ResolveTermsForActivation"/>).
    /// </summary>
    private static IEnumerable<ContractMismatchReason> CompareTerms(ContractTerms a, ContractTerms b)
    {
        if (a.Material != b.Material)
        {
            yield return ContractMismatchReason.MaterialDiffers;
        }
        if (a.Type != b.Type)
        {
            yield return ContractMismatchReason.TypeDiffers;
        }
        if (a.Volume != b.Volume)
        {
            yield return ContractMismatchReason.VolumeDiffers;
        }
        if (a.UnitPrice != b.UnitPrice)
        {
            yield return ContractMismatchReason.UnitPriceDiffers;
        }
        if (a.PenaltyRate != b.PenaltyRate)
        {
            yield return ContractMismatchReason.PenaltyRateDiffers;
        }
        if (a.SpotDeliveryTurn != b.SpotDeliveryTurn || a.RecurringEndTurn != b.RecurringEndTurn)
        {
            yield return ContractMismatchReason.DeliveryScheduleDiffers;
        }
    }
}
