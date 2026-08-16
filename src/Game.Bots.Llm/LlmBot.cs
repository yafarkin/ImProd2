using System.Diagnostics;
using System.Text;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>
/// Один LLM-бот, ведущий одну команду поперёк ходов (шаг 4 плана, docs/TODO.md #20) — собирает
/// системный (<see cref="SystemPromptBuilder"/>) и пользовательский (<see cref="BotStateSnapshotBuilder"/>
/// + <see cref="BotHistorySeriesBuilder"/> + собственная <see cref="BotTurnHistory"/>) промпты
/// заново на каждый ход и прогоняет их через <see cref="LlmBotDecisionLoop"/>. Каждый вызов к
/// <see cref="ILlmClient"/> внутри цикла — без накопленного контекста (решение пользователя), но
/// сам <see cref="LlmBot"/> помнит итоги своих прошлых ходов между вызовами
/// <see cref="TakeTurnAsync"/> — так модель на следующем ходу видит, что делала раньше и почему
/// (собственные аннотации), не имея настоящей памяти между запросами.
/// </summary>
public sealed class LlmBot
{
    private readonly LlmBotDecisionLoop _loop;
    private readonly BotTurnHistory _history;
    private readonly int _historySampleInterval;

    /// <summary>Команда, которой управляет этот бот.</summary>
    public Ulid TeamId { get; }

    /// <summary>Текст персоны (страх/жадность и любые другие устойчивые черты) — часть системного промпта на каждый ход.</summary>
    public string PersonaDescription { get; }

    /// <summary><paramref name="historySampleInterval"/> — шаг разреженной экономической истории по ходам, см. <see cref="BotHistorySeriesBuilder"/>.</summary>
    public LlmBot(
        Ulid teamId, string personaDescription, ILlmClient client, int maxAttempts = 3, int historyWindow = 10, int historySampleInterval = 5)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (string.IsNullOrWhiteSpace(personaDescription))
        {
            throw new ArgumentException("Persona description must not be empty.", nameof(personaDescription));
        }

        TeamId = teamId;
        PersonaDescription = personaDescription;
        _loop = new LlmBotDecisionLoop(client, new BotCommandExecutor(), maxAttempts);
        _history = new BotTurnHistory(historyWindow);
        _historySampleInterval = historySampleInterval;
    }

    /// <summary>Итоги прошлых ходов этого бота, в пределах окна истории — самая старая запись первая.</summary>
    public IReadOnlyList<BotTurnHistoryEntry> History => _history.Entries;

    /// <summary>
    /// Собирает промпты по текущему состоянию <paramref name="session"/>, прогоняет цикл
    /// execute→validate→retry и запоминает итог хода в собственной истории для будущих ходов.
    /// <paramref name="metricsLog"/> необязателен (тесты и разовые прогоны без файла метрик его не
    /// передают) — если задан, добавляет одну строку в <see cref="BotMetricsLog"/>: время ответа
    /// покрывает весь ход целиком (включая все попытки ретрая внутри него, если они были), размер
    /// запроса — байты первой (без текста ошибок) пары систем+user промпта этого хода, см.
    /// doc-comment <see cref="BotMetricsLog"/>.
    /// </summary>
    public async Task<LlmBotTurnResult> TakeTurnAsync(
        GameSession session, BotDecisionLog log, BotMetricsLog? metricsLog = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);

        var systemPrompt = SystemPromptBuilder.Build(PersonaDescription);
        var stateSnapshot = BotStateSnapshotBuilder.Build(session, TeamId);
        var historySeries = BotHistorySeriesBuilder.Build(session, TeamId, _historySampleInterval);
        var userPrompt = $"{stateSnapshot}\n{historySeries}\n{_history.Render()}";
        var turn = session.State.CurrentTurn;

        var stopwatch = metricsLog is null ? null : Stopwatch.StartNew();
        var result = await _loop.RunTurnAsync(session, TeamId, systemPrompt, userPrompt, log, cancellationToken).ConfigureAwait(false);
        stopwatch?.Stop();

        var summary = BotCommandSummary.Describe(result);
        _history.Add(new BotTurnHistoryEntry(turn, summary, result.Command?.Annotation));

        if (metricsLog is not null)
        {
            var botLabel = session.State.Teams.TryGetValue(TeamId, out var team) ? team.Name : TeamId.ToString();
            var requestSizeBytes = Encoding.UTF8.GetByteCount(systemPrompt) + Encoding.UTF8.GetByteCount(userPrompt);
            metricsLog.Record(botLabel, turn, stopwatch!.Elapsed, requestSizeBytes, summary);
        }

        return result;
    }
}
