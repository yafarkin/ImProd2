using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Ответ на предложение пересмотра условий контракта (Блок 9.3, SPEC §6). При принятии — старый
/// контракт расторгается без штрафа (обе стороны уже согласились, см.
/// <see cref="ContractTerminationReason.Mutual"/>) и заводится новый, сразу действующий — отдельное
/// подтверждение через <see cref="ContractConfirmed"/> не нужно, само принятие уже и есть согласие
/// обеих сторон. При отказе — состояние не меняется вовсе, контракт продолжает действовать на
/// прежних условиях без какого-либо штрафа за сам факт предложения.
/// </summary>
public sealed record ContractRevisionResolved : Change<GameSessionState>
{
    /// <summary>Контракт, к предложению по которому относится ответ.</summary>
    public required Ulid ContractId { get; init; }

    /// <summary>Принято ли предложение.</summary>
    public required bool Accepted { get; init; }

    /// <summary>Снимок контракта-замены — заполнен только при <see cref="Accepted"/>.</summary>
    public required ContractSpec? ReplacementContract { get; init; }

    public override void Apply(GameSessionState state)
    {
        if (!Accepted)
        {
            return;
        }

        state.Contracts[ContractId].Terminate(ContractTerminationReason.Mutual);

        var replacement = ReplacementContract!.ToContract(state);
        replacement.ConfirmAutomatically(); // обе стороны уже согласились самим принятием предложения
        state.AddContract(replacement);
    }
}
