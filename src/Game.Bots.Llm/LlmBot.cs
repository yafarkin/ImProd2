using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>
/// Один LLM-бот, ведущий одну команду поперёк ходов (шаг 4 плана, docs/TODO.md #20) — собирает
/// системный (<see cref="SystemPromptBuilder"/>) и пользовательский (<see cref="BotStateSnapshotBuilder"/>
/// + собственная <see cref="BotTurnHistory"/>) промпты заново на каждый ход и прогоняет их через
/// <see cref="LlmBotDecisionLoop"/>. Каждый вызов к <see cref="ILlmClient"/> внутри цикла — без
/// накопленного контекста (решение пользователя), но сам <see cref="LlmBot"/> помнит итоги своих
/// прошлых ходов между вызовами <see cref="TakeTurnAsync"/> — так модель на следующем ходу видит,
/// что делала раньше и почему (собственные аннотации), не имея настоящей памяти между запросами.
/// </summary>
public sealed class LlmBot
{
    private readonly LlmBotDecisionLoop _loop;
    private readonly BotTurnHistory _history;

    /// <summary>Команда, которой управляет этот бот.</summary>
    public Ulid TeamId { get; }

    /// <summary>Текст персоны (страх/жадность и любые другие устойчивые черты) — часть системного промпта на каждый ход.</summary>
    public string PersonaDescription { get; }

    public LlmBot(Ulid teamId, string personaDescription, ILlmClient client, int maxAttempts = 3, int historyWindow = 10)
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
    }

    /// <summary>Итоги прошлых ходов этого бота, в пределах окна истории — самая старая запись первая.</summary>
    public IReadOnlyList<BotTurnHistoryEntry> History => _history.Entries;

    /// <summary>
    /// Собирает промпты по текущему состоянию <paramref name="session"/>, прогоняет цикл
    /// execute→validate→retry и запоминает итог хода в собственной истории для будущих ходов.
    /// </summary>
    public async Task<LlmBotTurnResult> TakeTurnAsync(GameSession session, BotDecisionLog log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);

        var systemPrompt = SystemPromptBuilder.Build(PersonaDescription);
        var stateSnapshot = BotStateSnapshotBuilder.Build(session, TeamId);
        var userPrompt = $"{stateSnapshot}\n\n{_history.Render()}";
        var turn = session.State.CurrentTurn;

        var result = await _loop.RunTurnAsync(session, TeamId, systemPrompt, userPrompt, log, cancellationToken).ConfigureAwait(false);

        _history.Add(new BotTurnHistoryEntry(turn, BotCommandSummary.Describe(result), result.Command?.Annotation));

        return result;
    }
}
