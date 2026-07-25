using System.Text.Json;

namespace Game.Config.Loading;

/// <summary>
/// Сериализует GameConfig в JSON — обратная операция к <see cref="GameConfigLoader"/>. Не
/// валидирует: предполагается, что сохраняется уже валидный конфиг (например, тот, что получен
/// через успешную загрузку, — <see cref="ResolvedGameConfig.Raw"/>).
/// </summary>
public static class GameConfigWriter
{
    private static readonly JsonSerializerOptions DefaultOptions = new() { WriteIndented = true };

    /// <summary>Сериализует GameConfig в строку JSON (по умолчанию — с отступами, для ручного редактирования).</summary>
    public static string Save(GameConfig config, JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        return JsonSerializer.Serialize(config, serializerOptions ?? DefaultOptions);
    }

    /// <summary>Сериализует GameConfig в JSON и записывает по указанному пути, перезаписывая файл, если он уже есть.</summary>
    public static void SaveToFile(GameConfig config, string path, JsonSerializerOptions? serializerOptions = null)
    {
        File.WriteAllText(path, Save(config, serializerOptions));
    }
}
