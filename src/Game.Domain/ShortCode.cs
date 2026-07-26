namespace Game.Domain;

/// <summary>
/// Короткий буквенно-цифровой код общего назначения — без символов, которые легко перепутать на
/// слух/письме (0/O, 1/I/l и т.п.). Используется и для подтверждения сделок
/// (<see cref="ContractConfirmationCode"/>), и для входа участников по коду (Блок 8.1, SPEC §3) —
/// разные по смыслу сущности, но одна и та же форма кода.
/// </summary>
public static class ShortCode
{
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
