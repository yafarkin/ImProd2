namespace Game.Engine;

/// <summary>
/// Оператор подтвердил сделку по коду (Блок 9.5, SPEC §6, §9.4) — второй, равноправный путь к
/// тому же результату, что и <see cref="ContractConfirmed"/> (подтверждение управляющим на
/// дашборде команды). Какой путь сработает первым — тот и подтвердил.
/// </summary>
public sealed record ContractConfirmedByOperator : Change<GameSessionState>
{
    /// <summary>Идентификатор подтверждаемого контракта.</summary>
    public required Ulid ContractId { get; init; }

    public override void Apply(GameSessionState state) => state.Contracts[ContractId].ConfirmByOperator();
}
