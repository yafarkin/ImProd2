using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Bots.Llm;

/// <summary>
/// Проверка ожидающего ответа остановленного файлового опыта (docs/ASSISTANT-BOT-NEXT-STEP.md,
/// docs/ASSISTANT-BOT.md). Класс НЕ исполняет команды и не меняет файлы: возвращает проверенные
/// байты уже одобренного счётчиком ответа, а их исполнение — консоль (`--file-resume`) поверх
/// восстановленного из durable-журнала `GameSession`. Генератор случайности после восстановления
/// не сохранён (AGENTS §2, правило 6), поэтому ответ со случайно зависимой командой
/// <see cref="BotCommandKind.FulfillTradeOffer"/> отвергается до исполнения.
/// </summary>
public static class FileBotResume
{
    /// <summary>Проверенный ответ на ожидающий ход: байты совпадают с одобрением счётчика.</summary>
    public sealed record PendingDecision(int Turn, string ResponseJson);

    /// <summary>
    /// Ищет и проверяет ответ на ход <paramref name="turn"/> в каталоге прогона — тот же тройной
    /// набор файлов и та же проверка SHA-256 байтов, что и в живом цикле <c>FileBotExperiment</c>.
    /// Возвращает <c>null</c>, если на этот ход ответ ещё не одобрен (запрос выдан, но решение
    /// не подано): исполнение невозможно, а не «пустое». Бросает, если одобрение не совпадает
    /// с байтами ответа, конверт не соответствует ходу либо ответ содержит случайные команды.
    /// </summary>
    public static PendingDecision? FindApproved(string directory, int turn)
    {
        var stem = Path.Combine(directory, $"{turn:000}");
        if (!File.Exists($"{stem}.request.json"))
        {
            return null; // ход ещё не открывал фазу решений — запроса не существует
        }
        if (!File.Exists($"{stem}.response.json") || !File.Exists($"{stem}.approved"))
        {
            return null; // запрос выдан, ответ одобрен счётчиком не был — исполнять нечего
        }

        var responseJson = File.ReadAllText($"{stem}.response.json");
        var approved = File.ReadAllText($"{stem}.approved").Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(responseJson)));
        if (!string.Equals(approved, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Approved marker of turn {turn} does not match response bytes.");
        }

        VerifyEnvelope(responseJson, turn);
        return new(turn, responseJson);
    }

    /// <summary>
    /// Проверяет конверт ответа (requestId, разбор команд) и отвергает случайные команды.
    /// Разбор — через тот же <see cref="BotCommandSerialization.Options"/>, что и исполнение,
    /// чтобы проверка видела ровно те же <see cref="BotCommand"/>. Псевдоним `f*`/`new:*` здесь
    /// не проверяется: он разрешается при исполнении и на случайность не влияет.
    /// </summary>
    public static void VerifyEnvelope(string responseJson, int turn)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (document.RootElement.GetProperty("requestId").GetString() != $"turn-{turn}")
        {
            throw new InvalidOperationException($"Response requestId does not match turn {turn}.");
        }
        foreach (var action in document.RootElement.GetProperty("actions").EnumerateArray())
        {
            var command = action.Deserialize<BotCommand>(BotCommandSerialization.Options)
                ?? throw new JsonException("Null command.");
            if (command.Kind == BotCommandKind.FulfillTradeOffer)
            {
                throw new InvalidOperationException(
                    $"Turn {turn} response contains '{BotCommandKind.FulfillTradeOffer}', whose " +
                    "execution depends on Random; a resumed run cannot reproduce it.");
            }
        }
    }
}
