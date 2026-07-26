namespace Game.Domain;

/// <summary>
/// Короткий код подтверждения, который команды переносят на бумажный бланк вместе с условиями
/// сделки (SPEC §6, §9.4) — оператор сверяет по нему, не вводя условия вручную.
/// </summary>
public static class ContractConfirmationCode
{
    /// <summary>Генерирует код — см. <see cref="ShortCode.Generate"/> для формы и правил случайности.</summary>
    public static string Generate(Random random) => ShortCode.Generate(random);
}
