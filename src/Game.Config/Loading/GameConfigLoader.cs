using System.Text.Json;
using Game.Config.ProductionModel;
using Game.Config.Session;

namespace Game.Config.Loading;

/// <summary>
/// Загружает GameConfig из JSON, валидирует ссылочную целостность и строит объектный граф
/// Game.Domain. Единственная точка входа для загрузки конфига сессии.
///
/// Два семейства перегрузок: <see cref="Load(string)"/>/<see cref="LoadFromFile(string)"/>
/// принимают один уже собранный <see cref="GameConfig"/> (так персистируется запущенная сессия —
/// <c>config.json</c> — и так удобно править/загружать свой файл целиком); <see
/// cref="Load(ProductionModelConfig, SessionConfig)"/>/<see
/// cref="LoadFromFiles(string, string)"/> собирают его из пары авторских файлов — производственной
/// модели и сессионных параметров (<see cref="GameConfigComposer"/>) — так администратор выбирает
/// их независимо друг от друга перед стартом сессии.
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

        return Load(config);
    }

    /// <summary>Валидирует и резолвит уже десериализованный GameConfig (например, собранный <see cref="GameConfigComposer"/>).</summary>
    public static ResolvedGameConfig Load(GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = GameConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new GameConfigValidationException(errors);
        }

        return GameConfigResolver.Resolve(config);
    }

    /// <summary>Собирает производственную модель и сессионные параметры (<see cref="GameConfigComposer"/>), валидирует и резолвит результат.</summary>
    public static ResolvedGameConfig Load(ProductionModelConfig productionModel, SessionConfig session)
    {
        return Load(GameConfigComposer.Compose(productionModel, session));
    }

    /// <summary>Читает производственную модель и сессионные параметры из пары файлов и собирает их в один конфиг (<see cref="Load(ProductionModelConfig, SessionConfig)"/>).</summary>
    public static ResolvedGameConfig LoadFromFiles(string productionModelPath, string sessionPath)
    {
        return Load(LoadProductionModelFromFile(productionModelPath), LoadSessionFromFile(sessionPath));
    }

    /// <summary>
    /// Читает производственную модель из файла без сборки в <see cref="GameConfig"/> — для списка
    /// вариантов, которые можно выбрать и скомбинировать (например, экран администратора).
    /// Ссылочную целостность модели саму по себе можно проверить отдельно через <see
    /// cref="GameConfigValidator.ValidateProductionModel"/>.
    /// </summary>
    public static ProductionModelConfig LoadProductionModelFromFile(string path) => DeserializeFile<ProductionModelConfig>(path);

    /// <summary>Читает сессионные параметры из файла без сборки в <see cref="GameConfig"/> — симметрично <see cref="LoadProductionModelFromFile"/>.</summary>
    public static SessionConfig LoadSessionFromFile(string path) => DeserializeFile<SessionConfig>(path);

    private static T DeserializeFile<T>(string path)
    {
        var json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize<T>(json)
                   ?? throw new GameConfigValidationException(new[] { $"'{path}' deserialized to null." });
        }
        catch (JsonException ex)
        {
            throw new GameConfigValidationException(new[] { $"'{path}' is malformed: {ex.Message}" });
        }
    }
}
