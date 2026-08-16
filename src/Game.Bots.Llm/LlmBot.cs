using System.Diagnostics;
using System.Text;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>
/// Итог целого хода одного <see cref="LlmBot"/> — список действий (может быть несколько подряд, см.
/// doc-comment класса <see cref="LlmBot"/>), в порядке принятия.
/// </summary>
public sealed record LlmBotTurnReport(int Turn, IReadOnlyList<LlmBotTurnResult> Actions)
{
    /// <summary>Сколько действий за ход реально исполнилось (не считая финальный nop/exhausted).</summary>
    public int SuccessfulActionCount => Actions.Count(a => a.Outcome == LlmBotTurnOutcome.Success);

    /// <summary>Ход закончился явным «готово» от модели, а не потолком попыток/действий.</summary>
    public bool EndedWithNop => Actions.Count > 0 && Actions[^1].Outcome == LlmBotTurnOutcome.Nop;

    /// <summary>
    /// Провал хода целиком — не получилось ни одного валидного действия (даже первого). Именно это,
    /// а не любое исчерпание попыток внутри хода с уже успевшими пройти действиями, должно считаться
    /// «плохим ходом» для остановки прогона в <see cref="LlmBotSessionRunner"/> — бот, взявший заём и
    /// только потом застрявший на второй команде, всё же продвинулся, а не просто застрял.
    /// </summary>
    public bool IsFullyFailedTurn => SuccessfulActionCount == 0 && Actions.Count > 0 && Actions[^1].Outcome == LlmBotTurnOutcome.Exhausted;
}

/// <summary>
/// Один LLM-бот, ведущий одну команду поперёк ходов (шаг 4 плана, docs/TODO.md #20) — собирает
/// системный (<see cref="SystemPromptBuilder"/>) и пользовательский (<see cref="BotStateSnapshotBuilder"/>
/// + <see cref="BotHistorySeriesBuilder"/> + собственная <see cref="BotTurnHistory"/>) промпты
/// заново на каждое действие и прогоняет их через <see cref="LlmBotDecisionLoop"/>. Каждый вызов к
/// <see cref="ILlmClient"/> — без накопленного контекста (решение пользователя), но сам
/// <see cref="LlmBot"/> помнит итоги своих прошлых ходов между вызовами <see cref="TakeTurnAsync"/>
/// — так модель на следующем ходу видит, что делала раньше и почему (собственные аннотации), не
/// имея настоящей памяти между запросами.
/// <para>
/// Один ход — не обязательно одно действие (запрос пользователя 2026-08-16: «важно уметь несколько
/// команд за один ход», реальный игрок за фазу решений успевает построить, нанять, поднять R&amp;D
/// подряд, не по одному действию на ход). <see cref="TakeTurnAsync"/> поэтому сам зовёт LLM
/// повторно — с каждым разом показывая модели, что она уже решила в ЭТОМ ходу («THIS TURN» в
/// промпте, отдельно от <see cref="BotTurnHistory"/> — та про прошлые ходы), — пока модель не
/// ответит <c>nop</c> («готово на этот ход») или не сработает страховка
/// <see cref="_maxActionsPerTurn"/> (на случай, если модель не научится сама вовремя останавливаться
/// — это и предстоит проверить живым прогоном).
/// </para>
/// </summary>
public sealed class LlmBot
{
    private readonly LlmBotDecisionLoop _loop;
    private readonly BotTurnHistory _history;
    private readonly int _historySampleInterval;
    private readonly int _maxActionsPerTurn;

    /// <summary>Команда, которой управляет этот бот.</summary>
    public Ulid TeamId { get; }

    /// <summary>Текст персоны (страх/жадность и любые другие устойчивые черты) — часть системного промпта на каждый ход.</summary>
    public string PersonaDescription { get; }

    /// <summary>
    /// <paramref name="historySampleInterval"/> — шаг разреженной экономической истории по ходам, см.
    /// <see cref="BotHistorySeriesBuilder"/>. <paramref name="maxActionsPerTurn"/> — страховочный
    /// потолок действий за один ход (см. doc-comment класса) — не то, чем должен управляться обычный
    /// ход, модель должна сама остановиться через <c>nop</c> задолго до него.
    /// </summary>
    public LlmBot(
        Ulid teamId, string personaDescription, ILlmClient client, int maxAttempts = 3, int historyWindow = 10,
        int historySampleInterval = 5, int maxActionsPerTurn = 8)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (string.IsNullOrWhiteSpace(personaDescription))
        {
            throw new ArgumentException("Persona description must not be empty.", nameof(personaDescription));
        }
        if (maxActionsPerTurn < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxActionsPerTurn), maxActionsPerTurn, "Must allow at least one action per turn.");
        }

        TeamId = teamId;
        PersonaDescription = personaDescription;
        _loop = new LlmBotDecisionLoop(client, new BotCommandExecutor(), maxAttempts);
        _history = new BotTurnHistory(historyWindow);
        _historySampleInterval = historySampleInterval;
        _maxActionsPerTurn = maxActionsPerTurn;
    }

    /// <summary>Итоги прошлых ходов этого бота, в пределах окна истории — самая старая запись первая.</summary>
    public IReadOnlyList<BotTurnHistoryEntry> History => _history.Entries;

    /// <summary>
    /// Прогоняет весь ход — один или несколько действий подряд, пока модель не ответит <c>nop</c>
    /// или не сработает потолок <see cref="_maxActionsPerTurn"/> — и запоминает итог в собственной
    /// истории для будущих ходов. <paramref name="onStatusLine"/> — необязательный построчный лог
    /// «что происходит прямо сейчас» (запрос пользователя 2026-08-16): по паре строк на каждое
    /// действие («запрос к LLM...» / итог с затраченным временем). <paramref name="metricsLog"/> —
    /// пишет по одной строке <see cref="BotMetricsLog"/> на каждое действие (не на весь ход).
    /// </summary>
    public async Task<LlmBotTurnReport> TakeTurnAsync(
        GameSession session, BotDecisionLog log, BotMetricsLog? metricsLog = null,
        Action<string>? onStatusLine = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);

        var turn = session.State.CurrentTurn;
        var botLabel = session.State.Teams.TryGetValue(TeamId, out var teamAtStart) ? teamAtStart.Name : TeamId.ToString();
        var actionsThisTurn = new List<BotTurnActionRecord>();
        var results = new List<LlmBotTurnResult>();
        var hitActionCap = true;

        for (var actionIndex = 1; actionIndex <= _maxActionsPerTurn; actionIndex++)
        {
            var systemPrompt = SystemPromptBuilder.Build(PersonaDescription);
            var stateSnapshot = BotStateSnapshotBuilder.Build(session, TeamId);
            var historySeries = BotHistorySeriesBuilder.Build(session, TeamId, _historySampleInterval);
            var thisTurnSoFar = RenderThisTurnSoFar(actionsThisTurn, actionIndex);
            var userPrompt = $"{stateSnapshot}\n{historySeries}\n{thisTurnSoFar}\n{_history.Render()}";

            onStatusLine?.Invoke($"{botLabel}: ход {turn}, действие {actionIndex} — запрос к LLM...");

            var stopwatch = Stopwatch.StartNew();
            var result = await _loop.RunTurnAsync(session, TeamId, systemPrompt, userPrompt, log, actionIndex, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var summary = BotCommandSummary.Describe(result);
            onStatusLine?.Invoke(
                $"{botLabel}: ход {turn}, действие {actionIndex} — {result.Outcome} за {stopwatch.Elapsed:mm\\:ss} — {summary}");

            if (metricsLog is not null)
            {
                var requestSizeBytes = Encoding.UTF8.GetByteCount(systemPrompt) + Encoding.UTF8.GetByteCount(userPrompt);
                var (balance, debt, factoryCount) = session.State.Teams.TryGetValue(TeamId, out var teamNow)
                    ? (teamNow.Balance, teamNow.Debt, teamNow.Factories.Count)
                    : (0m, 0m, 0);
                metricsLog.Record(botLabel, turn, actionIndex, stopwatch.Elapsed, requestSizeBytes, summary, balance, debt, factoryCount);
            }

            results.Add(result);
            actionsThisTurn.Add(new BotTurnActionRecord(summary, result.Command?.Annotation));

            if (result.Outcome is LlmBotTurnOutcome.Nop or LlmBotTurnOutcome.Exhausted)
            {
                hitActionCap = false;
                break;
            }
        }

        if (hitActionCap)
        {
            onStatusLine?.Invoke(
                $"{botLabel}: ход {turn} — достигнут потолок {_maxActionsPerTurn} действий за ход, не дождались nop");
        }

        _history.Add(new BotTurnHistoryEntry(turn, actionsThisTurn));

        return new LlmBotTurnReport(turn, results);
    }

    /// <summary>«THIS TURN» — что бот уже решил в РАМКАХ ЭТОГО хода (не путать с <see cref="BotTurnHistory"/> — той про прошлые ходы).</summary>
    private static string RenderThisTurnSoFar(IReadOnlyList<BotTurnActionRecord> actionsSoFar, int nextActionIndex)
    {
        var header = $"=== THIS TURN (deciding action {nextActionIndex}) ===";
        if (actionsSoFar.Count == 0)
        {
            return $"{header}\nYou have taken no actions yet this turn. Decide the first one, or respond " +
                "kind=\"nop\" if you genuinely have nothing to do.";
        }

        var lines = actionsSoFar.Select((a, i) => a.Annotation is null
            ? $"- Action {i + 1}: {a.Summary}"
            : $"- Action {i + 1}: {a.Summary} — {a.Annotation}");

        return $"{header}\nActions already taken this turn:\n{string.Join('\n', lines)}\n" +
            "Decide the next action, or respond kind=\"nop\" once you are done for this turn.";
    }
}
