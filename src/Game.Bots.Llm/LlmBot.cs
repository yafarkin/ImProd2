using System.Diagnostics;
using System.Text;
using Game.Engine;

namespace Game.Bots.Llm;

/// <summary>
/// Итог целого хода одного <see cref="LlmBot"/> — список действий одного вызова LLM (запрос
/// пользователя 2026-08-16: один вызов на весь ход, см. doc-comment <see cref="LlmBot"/>), в порядке
/// исполнения.
/// </summary>
public sealed record LlmBotTurnReport(int Turn, IReadOnlyList<LlmBotTurnResult> Actions)
{
    /// <summary>Сколько действий за ход реально исполнилось.</summary>
    public int SuccessfulActionCount => Actions.Count(a => a.Outcome == LlmBotTurnOutcome.Success);

    /// <summary>Ход закончился явным «готово» от модели (пустой массив действий), а не провалом/пропуском.</summary>
    public bool EndedWithNop => Actions.Count > 0 && Actions[^1].Outcome == LlmBotTurnOutcome.Nop;

    /// <summary>
    /// Провал хода целиком — не получилось ни одного валидного действия, и модель НЕ завершила ход
    /// явным <c>nop</c> (пустым массивом) — то есть либо не удалось распарсить ответ вовсе
    /// (<see cref="LlmBotTurnOutcome.Exhausted"/>), либо все предложенные действия были отклонены
    /// (<see cref="LlmBotTurnOutcome.Skipped"/>), а не то, что модель осознанно решила ничего не
    /// делать. Именно это, не любой отдельный пропуск, должно считаться «плохим ходом» для остановки
    /// прогона в <see cref="LlmBotSessionRunner"/> — бот, успешно построивший фабрику, а остальное
    /// предложивший невалидным, всё же продвинулся, а не просто застрял.
    /// </summary>
    public bool IsFullyFailedTurn => SuccessfulActionCount == 0 && Actions.Count > 0 && Actions[^1].Outcome != LlmBotTurnOutcome.Nop;
}

/// <summary>
/// Один LLM-бот, ведущий одну команду поперёк ходов (шаг 4 плана, docs/TODO.md #20) — собирает
/// системный (<see cref="SystemPromptBuilder"/>) и пользовательский (<see cref="BotStateSnapshotBuilder"/>
/// + <see cref="BotDerivedMetricsBuilder"/> + <see cref="BotHistorySeriesBuilder"/> + собственная
/// <see cref="BotTurnHistory"/>) промпты и
/// прогоняет их через <see cref="LlmBotDecisionLoop"/>. Каждый вызов к <see cref="ILlmClient"/> — без
/// накопленного контекста (решение пользователя), но сам <see cref="LlmBot"/> помнит итоги своих
/// прошлых ходов между вызовами <see cref="TakeTurnAsync"/> — так модель на следующем ходу видит, что
/// делала раньше и почему (собственные аннотации, включая причину пропуска — см.
/// <see cref="LlmBotTurnResult.ForSkipped"/>), не имея настоящей памяти между запросами.
/// <para>
/// Один ход — не обязательно одно действие (запрос пользователя 2026-08-16: «важно уметь несколько
/// команд за один ход», реальный игрок за фазу решений успевает построить, нанять, поднять R&amp;D
/// подряд), но с 2026-08-16 это РОВНО ОДИН вызов LLM за ход (тот же день, более поздний запрос
/// пользователя: «только раз за ход обращаться к LLM, и чтобы он сразу формировал массив команд на
/// ход» — многовызовная версия была и дороже по токенам, и не мешала модели закапываться в один и тот
/// же неудачный паттерн раз за разом внутри хода). Модель декларирует весь план хода одним JSON-
/// массивом (<see cref="BotCommandBatch"/>), <see cref="LlmBotDecisionLoop"/> исполняет его по
/// порядку. Явное ограничение отсюда: команда, нацеленная на факти, построенную РАНЕЕ В ЭТОМ ЖЕ
/// массиве (например, <c>setWorkerCount</c> сразу после <c>buildFactory</c> для неё же), не может
/// сработать — движок выдаёт настоящий id фабрики только в момент её постройки, до этого его просто
/// не существует, чтобы модель могла его назвать. Это озвучено в <see cref="SystemPromptBuilder"/> —
/// построил в этом ходу, нанял в следующем, не баг, осознанное ограничение батч-режима.
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
    /// <see cref="BotHistorySeriesBuilder"/>. <paramref name="maxActionsPerTurn"/> — потолок длины
    /// массива действий за один вызов (см. doc-comment класса) — избыток сверх него молча отбрасывается.
    /// <paramref name="initialHistory"/> — восстановление собственной памяти бота после прерывания
    /// прогона (запрос пользователя 2026-08-19), см. doc-comment <see cref="BotTurnHistory"/>.
    /// </summary>
    public LlmBot(
        Ulid teamId, string personaDescription, ILlmClient client, int maxAttempts = 3, int historyWindow = 10,
        int historySampleInterval = 5, int maxActionsPerTurn = 5, IReadOnlyList<BotTurnHistoryEntry>? initialHistory = null)
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
        _history = new BotTurnHistory(historyWindow, initialHistory);
        _historySampleInterval = historySampleInterval;
        _maxActionsPerTurn = maxActionsPerTurn;
    }

    /// <summary>Итоги прошлых ходов этого бота, в пределах окна истории — самая старая запись первая.</summary>
    public IReadOnlyList<BotTurnHistoryEntry> History => _history.Entries;

    /// <summary>
    /// Прогоняет весь ход — ровно один вызов LLM (см. doc-comment класса), возвращающий массив
    /// действий, исполняемых по порядку — и запоминает итог в собственной истории для будущих ходов.
    /// <paramref name="onStatusLine"/> — необязательный построчный лог «что происходит прямо сейчас»
    /// (запрос пользователя 2026-08-16): одна строка на запрос к LLM, затем по одной на каждое
    /// действие из полученного массива. <paramref name="metricsLog"/> — пишет по одной строке
    /// <see cref="BotMetricsLog"/> на каждое действие; время и размер запроса — общие для всего
    /// вызова (единственного за ход), намеренно одинаковые в каждой строке одного хода.
    /// <paramref name="random"/> — тот же общий генератор, что и у <see cref="Game.Engine.GameSession.RunTick"/>
    /// в <see cref="LlmBotSessionRunner"/>, нужен только команде <see cref="BotCommandKind.FulfillTradeOffer"/>
    /// (код подтверждения контракта) — прокидывается насквозь, чтобы журнал сессии оставался
    /// воспроизводимым при одном и том же сиде (AGENTS §2, правило 6).
    /// </summary>
    public async Task<LlmBotTurnReport> TakeTurnAsync(
        GameSession session, BotDecisionLog log, Random random, BotMetricsLog? metricsLog = null,
        Action<string>? onStatusLine = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(random);

        var turn = session.State.CurrentTurn;
        var botLabel = session.State.Teams.TryGetValue(TeamId, out var teamAtStart) ? teamAtStart.Name : TeamId.ToString();

        // По занятым секторам, не по каталогу конфига — см. doc-comment BotStateSnapshotBuilder.AppendCrossSectorDemand.
        var hasMultipleSectors = session.State.Teams.Values.Select(t => t.Sector).Distinct().Count() > 1;
        var systemPrompt = SystemPromptBuilder.Build(PersonaDescription, _maxActionsPerTurn, hasMultipleSectors);
        var stateSnapshot = BotStateSnapshotBuilder.Build(session, TeamId);
        var derivedMetrics = BotDerivedMetricsBuilder.Build(session, TeamId, _historySampleInterval);
        var historySeries = BotHistorySeriesBuilder.Build(session, TeamId, _historySampleInterval);
        var userPrompt = $"{stateSnapshot}\n{derivedMetrics}\n{historySeries}\n{_history.Render()}";

        onStatusLine?.Invoke($"{botLabel}: ход {turn} — запрос к LLM...");

        var stopwatch = Stopwatch.StartNew();
        var results = await _loop.RunTurnAsync(session, TeamId, systemPrompt, userPrompt, log, _maxActionsPerTurn, random, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var requestSizeBytes = Encoding.UTF8.GetByteCount(systemPrompt) + Encoding.UTF8.GetByteCount(userPrompt);
        var actionsThisTurn = new List<BotTurnActionRecord>(results.Count);

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var summary = BotCommandSummary.Describe(result);
            onStatusLine?.Invoke(
                $"{botLabel}: ход {turn}, действие {i + 1}/{results.Count} за {stopwatch.Elapsed:mm\\:ss} — {result.Outcome} — {summary}");

            if (metricsLog is not null)
            {
                var (balance, factoryCount) = session.State.Teams.TryGetValue(TeamId, out var teamNow)
                    ? (teamNow.Balance, teamNow.Factories.Count)
                    : (0m, 0);
                metricsLog.Record(botLabel, turn, i + 1, stopwatch.Elapsed, requestSizeBytes, summary, balance, factoryCount);
            }

            actionsThisTurn.Add(new BotTurnActionRecord(summary, result.Command?.Annotation));
        }

        _history.Add(new BotTurnHistoryEntry(turn, actionsThisTurn));

        return new LlmBotTurnReport(turn, results);
    }
}
