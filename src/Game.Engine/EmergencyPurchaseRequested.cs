namespace Game.Engine;

/// <summary>
/// Команда объявила желаемый объём аварийной закупки материала на ближайший расчёт (SPEC §4, §5.3:
/// решения не применяются сразу) — тем же приёмом, что и <see cref="LoanTakeRequested"/>: само
/// объявление бесплатно и мгновенно видимое в UI, реальная покупка (<see cref="EmergencyPurchased"/>,
/// со всеми деньгами и складом) происходит один раз, на расчёте (<see cref="EmergencyPurchaseStep"/>).
/// Последнее объявление по этому материалу в пределах хода замещает предыдущее — упрощение
/// (запрос пользователя): раньше несколько закупок одного материала за один ход эскалировали цену
/// друг для друга, теперь команда просто объявляет итоговый объём один раз; штраф «давления» за
/// растягивание закупок по нескольким ходам остаётся, он считается на расчёте по фактической истории
/// уже применённых закупок. <see cref="Volume"/> = 0 — заявка снята.
/// </summary>
public sealed record EmergencyPurchaseRequested : Change<GameSessionState>
{
    /// <summary>Команда, объявившая заявку.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Код закупаемого материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>Желаемый объём закупки на ближайший расчёт; 0 — заявка снята.</summary>
    public required decimal Volume { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Teams[TeamId].RequestEmergencyPurchase(MaterialId, Volume);
    }
}
