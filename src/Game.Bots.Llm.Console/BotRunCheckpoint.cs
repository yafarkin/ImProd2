using System.Text.Json;

namespace Game.Bots.Llm.ConsoleApp;

/// <summary>
/// Персона одного бота в чекпойнте — индекс в массиве персон <c>Program.cs</c>, не сам текст: персоны
/// заданы в коде, не меняются между запусками, дублировать текст в файл незачем.
/// </summary>
internal sealed record BotCheckpointEntry(string TeamId, int PersonaIndex, IReadOnlyList<BotTurnHistoryEntry> History);

/// <summary>
/// Всё, что нужно раннеру, чтобы продолжить прерванный прогон с того же места (запрос пользователя
/// 2026-08-19: «прервать Ctrl+C, потом запустить — и продолжит с прерванного места»). Игровое
/// состояние само по себе восстанавливается из durable-журнала (<see cref="Game.Persistence.DurableEventLog{TState}"/>,
/// уже проверенная в бою инфраструктура из <c>Game.Web</c>) — здесь только то, что журналом не
/// покрывается: пути к файлам ЭТОГО прогона (чтобы дописывать те же самые, не начинать новые с новой
/// меткой времени), сид генератора случайности (сам поток чисел после возобновления не совпадёт с
/// гипотетическим непрерывным прогоном — для качественного плейтеста это не важно, важна лишь
/// повторяемость вперёд от точки возобновления) и собственная память каждого бота
/// (<see cref="BotTurnHistory"/> — аннотации о прошлых ходах, не часть игрового журнала).
/// <para>
/// Переписывается целиком (не дозаписывается) после каждого хода — маленький файл, полная
/// перезапись дёшева и проще, чем инкрементальный формат. Запись — через временный файл и
/// атомарное переименование, тем же приёмом, что и <c>SnapshotFile</c> в <c>Game.Persistence</c>:
/// Ctrl+C или обрыв питания посреди записи не может оставить битый файл, только старую версию
/// целиком либо новую целиком.
/// </para>
/// </summary>
internal sealed record BotRunCheckpoint(
    int RandomSeed,
    string LogPath,
    string MetricsPath,
    string DecisionLogPath,
    string JournalPath,
    string SnapshotPath,
    IReadOnlyList<BotCheckpointEntry> Bots)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>Загружает чекпойнт, если файл есть; <see langword="null"/>, если предыдущего прогона не было (обычный старт с нуля).</summary>
    public static BotRunCheckpoint? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BotRunCheckpoint>(json, SerializerOptions);
    }

    /// <summary>Атомарно перезаписывает чекпойнт целиком — см. doc-comment класса.</summary>
    public void Save(string path)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(this, SerializerOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
