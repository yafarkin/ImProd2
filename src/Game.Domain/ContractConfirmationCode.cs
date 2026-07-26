namespace Game.Domain;

/// <summary>
/// Короткий код подтверждения, который команды переносят на бумажный бланк вместе с условиями
/// сделки (SPEC §6, §9.4) — оператор сверяет по нему, не вводя условия вручную.
/// </summary>
public static class ContractConfirmationCode
{
    // Без символов, которые легко перепутать на слух/письме: 0/O, 1/I/l и т.п.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 6;

    /// <summary>
    /// Генерирует код по явно переданному источнику случайности (AGENTS §2, правило 6 — никакой
    /// случайности без явного seed) — тот же принцип, что у розыгрыша хода окончания сессии
    /// в Game.Engine.
    /// </summary>
    public static string Generate(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[random.Next(Alphabet.Length)];
        }

        return new string(chars);
    }
}
