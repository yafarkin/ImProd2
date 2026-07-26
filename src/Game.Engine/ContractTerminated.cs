using Game.Domain;

namespace Game.Engine;

/// <summary>
/// Действующий контракт прекращён целиком (SPEC §6). Mutual (обоюдное) — без штрафа и без удара по
/// репутации (это не несоблюдение); voluntary (одностороннее) — инициатор платит фиксированную
/// плату <see cref="Fee"/> (намеренно высокий барьер) и несёт более тяжёлый, чем обычный срыв
/// поставки, удар по репутации (Блок 6.2, <see cref="ReputationCalculator"/> читает
/// <see cref="TerminatingTeamId"/> прямо из этого события). Сумма платы вычисляется до записи в
/// журнал и несётся событием, а не пересчитывается при применении.
/// </summary>
public sealed record ContractTerminated : Change<GameSessionState>
{
    /// <summary>Идентификатор прекращаемого контракта.</summary>
    public required Ulid ContractId { get; init; }

    /// <summary>Ход, на котором произошло прекращение — нужен модулю репутации (SPEC §7) для затухания по свежести.</summary>
    public required int Turn { get; init; }

    /// <summary>Причина прекращения — обоюдная или односторонняя.</summary>
    public required ContractTerminationReason Reason { get; init; }

    /// <summary>Команда-инициатор одностороннего расторжения — платит <see cref="Fee"/>. Null для mutual.</summary>
    public required Ulid? TerminatingTeamId { get; init; }

    /// <summary>Плата за одностороннее расторжение; 0 для mutual.</summary>
    public required decimal Fee { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Contracts[ContractId].Terminate(Reason);
        if (Fee > 0 && TerminatingTeamId is { } terminatingTeamId)
        {
            state.Teams[terminatingTeamId].Debit(Fee);
        }
    }
}
