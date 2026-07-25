using System.Text.Json;

namespace Game.Config;

/// <summary>
/// Загружает GameConfig из JSON, валидирует ссылочную целостность и строит объектный граф
/// Game.Domain. Единственная точка входа для загрузки конфига сессии.
/// </summary>
public static class GameConfigLoader
{
    /// <summary>Загружает и валидирует GameConfig из файла.</summary>
    public static ResolvedGameConfig LoadFromFile(string path)
    {
        return Load(File.ReadAllText(path));
    }

    /// <summary>Загружает и валидирует GameConfig из строки JSON.</summary>
    public static ResolvedGameConfig Load(string json)
    {
        GameConfig config;
        try
        {
            config = JsonSerializer.Deserialize<GameConfig>(json)
                     ?? throw new GameConfigValidationException(new[] { "GameConfig JSON deserialized to null." });
        }
        catch (JsonException ex)
        {
            throw new GameConfigValidationException(new[] { $"GameConfig JSON is malformed: {ex.Message}" });
        }

        var errors = GameConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new GameConfigValidationException(errors);
        }

        return GameConfigResolver.Resolve(config);
    }
}
