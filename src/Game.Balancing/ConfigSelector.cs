using System.Text.Json;
using Game.Config.Loading;

namespace Game.Balancing;

/// <summary>
/// Выбирает и загружает production-model цепочку за один вызов утилиты (Блок 7.3.3, BUILD_PLAN
/// «Фаза 7») — если <see cref="CliArguments.ConfigPath"/> не указан, короткий интерактивный список
/// файлов <c>Samples/production-models/*.json</c>, как и запросил пользователь. Сама цепочка целиком
/// (все секторы, которые в ней описаны) уходит в одну партию — не подмножество, см. doc-comment
/// <c>docs/TODO.md</c> №13 (сессия 2026-08-14).
/// </summary>
internal static class ConfigSelector
{
    private static string ProductionModelsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "production-models");

    private static string DefaultSessionPath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json");

    /// <summary>
    /// Грузит конфиг по <paramref name="args"/>: путь либо на уже полный <c>GameConfig</c> (собран
    /// целиком, как <c>legacy-combined-gameconfig.json</c>), либо на файл одной production-model цепочки без
    /// сессионных параметров — какой из двух перед нами, определяется по содержимому файла (наличие
    /// поля <c>Duration</c> верхнего уровня), не по флагу, так что один и тот же <c>--config</c>
    /// работает для обоих видов файлов без дополнительных подсказок.
    /// </summary>
    public static ResolvedGameConfig Load(CliArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var configPath = args.ConfigPath ?? PromptForProductionModel();
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found: '{configPath}'.", configPath);
        }

        var isFullGameConfig = args.SessionPath is null && IsFullGameConfig(configPath);

        // --difficulty переопределяет DifficultyLevel сессионного файла до сборки: грузим модель и
        // сессию по отдельности, подменяем уровень, собираем сами. Так одну цепочку можно прогнать
        // по всем целым уровням бегунка одной серией вызовов (docs/TODO.md №30).
        if (args.DifficultyLevel is { } difficultyOverride)
        {
            if (isFullGameConfig)
            {
                throw new ArgumentException(
                    $"'--difficulty' is not applicable to a fully-composed GameConfig ('{configPath}'): the difficulty " +
                    "slider only runs while composing a production model with session parameters (GameConfigComposer), " +
                    "not on a file that already went through it. Pass a production-model file (optionally with --session).");
            }

            var sessionPath = args.SessionPath ?? DefaultSessionPath;
            var productionModel = GameConfigLoader.LoadProductionModelFromFile(configPath);
            var session = GameConfigLoader.LoadSessionFromFile(sessionPath) with { DifficultyLevel = difficultyOverride };
            return GameConfigLoader.Load(productionModel, session);
        }

        if (args.SessionPath is { } explicitSessionPath)
        {
            return GameConfigLoader.LoadFromFiles(configPath, explicitSessionPath);
        }

        if (isFullGameConfig)
        {
            return GameConfigLoader.LoadFromFile(configPath);
        }

        // Файл production-model без сессионных параметров (Samples/production-models/*.json) —
        // достаём их из конфига по умолчанию, чтобы --config было единственным обязательным флагом.
        return GameConfigLoader.LoadFromFiles(configPath, DefaultSessionPath);
    }

    private static bool IsFullGameConfig(string configPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        return document.RootElement.TryGetProperty("Duration", out _);
    }

    private static string PromptForProductionModel()
    {
        var files = Directory.Exists(ProductionModelsDirectory)
            ? Directory.GetFiles(ProductionModelsDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList()
            : new List<string>();
        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"'--config' is not set and no production-model files were found under '{ProductionModelsDirectory}'.");
        }

        Console.WriteLine("Флаг --config не указан — выберите production-model цепочку (docs/production-staging.md):");
        for (var i = 0; i < files.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {Path.GetFileName(files[i])}");
        }
        Console.Write($"Номер (1-{files.Count}, Enter — {Path.GetFileName(files[0])}): ");

        var input = Console.ReadLine();
        if (int.TryParse(input, out var choice) && choice >= 1 && choice <= files.Count)
        {
            return files[choice - 1];
        }

        return files[0];
    }
}
