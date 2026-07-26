using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Config.Loading;

/// <summary>
/// Считает контент-хеш <see cref="GameConfig"/> — SHA-256 от его канонической сериализации.
/// Единственная реализация: журнал сессии записывает этот хеш в первую запись
/// (<c>SessionStarted</c>) и сверяет при восстановлении, чтобы лог был привязан к своему конфигу и
/// дрейф/подмена конфига обнаруживались (SPEC §11 — SHA-256, не MD5). Каноничность обеспечивается
/// фиксированными опциями сериализации: свойства System.Text.Json выдаёт в порядке объявления, а
/// секции GameConfig — массивы в авторском порядке, так что хеш детерминирован.
/// </summary>
public static class GameConfigHash
{
    private static readonly JsonSerializerOptions CanonicalOptions = new() { WriteIndented = false };

    /// <summary>Контент-хеш конфига в hex (SHA-256 от канонической сериализации <paramref name="raw"/>).</summary>
    public static string Compute(GameConfig raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var json = JsonSerializer.Serialize(raw, CanonicalOptions);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(hashBytes);
    }
}
