namespace Game.Engine;

/// <summary>
/// Списаны капитальные затраты за существование построенных фабрик команды за ход (амортизация,
/// охрана, аренда площадки, коммунальные услуги) — суммарно по всем фабрикам, вне зависимости от
/// числа рабочих и объёма выпуска (запрос пользователя: «платим за фабрику, даже если она вообще не
/// работает»). Переменная часть, растущая вместе с объёмом выпуска, списывается отдельно — см.
/// <see cref="FactoryProduced.OverheadCost"/>.
/// </summary>
public sealed record FactoryUpkeepPaid : Change<GameSessionState>
{
    /// <summary>Команда, оплатившая содержание фабрик.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Число построенных фабрик, за которые списано содержание — для аудита.</summary>
    public required int FactoryCount { get; init; }

    /// <summary>Списанная сумма.</summary>
    public required decimal Amount { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Teams[TeamId].Debit(Amount);
    }
}
