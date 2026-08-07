namespace Game.Engine;

/// <summary>
/// Команда объявила желаемый объём продажи материала системе на ближайший расчёт (SPEC §4, §5.4:
/// решения не применяются сразу) — симметрично <see cref="EmergencyPurchaseRequested"/>. Само
/// объявление бесплатно и мгновенно видимое в UI; реальная продажа (<see cref="MaterialSoldToSystem"/>,
/// со всеми деньгами, складом и расходом общей ёмкости рынка) происходит один раз, на расчёте (<see
/// cref="SystemSaleStep"/>), в детерминированном порядке команд — то самое лекарство от гонки за
/// общую ёмкость, ради которого и затевался весь перенос (SPEC §4). Последнее объявление по этому
/// материалу в пределах хода замещает предыдущее; в отличие от заявки на закупку, здесь дробление
/// объёма на несколько заявок ни на что не влияло бы даже без этого упрощения — расчёт продажи не
/// зависит от того, сколькими заявками набран общий объём, только от него самого. Реальный остаток на
/// складе на момент расчёта может быть меньше заявленного — урезается там же, без исключения (см.
/// doc-comment <see cref="MaterialSoldToSystem.Volume"/>). <see cref="Volume"/> = 0 — заявка снята.
/// </summary>
public sealed record MaterialSaleRequested : Change<GameSessionState>
{
    /// <summary>Команда, объявившая заявку.</summary>
    public required Ulid TeamId { get; init; }

    /// <summary>Код продаваемого материала.</summary>
    public required string MaterialId { get; init; }

    /// <summary>Желаемый объём продажи на ближайший расчёт; 0 — заявка снята.</summary>
    public required decimal Volume { get; init; }

    public override void Apply(GameSessionState state)
    {
        state.Teams[TeamId].RequestSaleToSystem(MaterialId, Volume);
    }
}
