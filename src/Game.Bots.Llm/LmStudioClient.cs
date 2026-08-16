using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Game.Bots.Llm;

/// <summary>
/// Реализация <see cref="ILlmClient"/> для LM Studio (шаг 2 плана LLM-ботов, docs/TODO.md #20) —
/// OpenAI-совместимый <c>/v1/chat/completions</c>, поэтому та же реализация без изменений подходит
/// и для Ollama (шаг 3), если у переданного <see cref="HttpClient"/> выставить его base address.
/// Ответ запрашивается как <c>response_format: json_schema</c> по <see cref="BotCommandSchema.BuildBatch"/>
/// (риск №2 из обсуждения TODO #20; запрос пользователя 2026-08-16 — один вызов LLM на весь ход,
/// массив команд разом, а не по одному действию на вызов, см. doc-comment <see cref="BotCommandBatch"/>)
/// — но <c>strict</c> сознательно <see langword="false"/> и
/// в required только <c>kind</c>: живая проверка на LM Studio (gemma-4-12b, 2026-08-16) показала,
/// что при <c>strict: true</c>, требующем перечислить в required вообще все поля схемы, модель не
/// умеет оставить нерелевантное поле пустым — вместо null подставляет правдоподобный мусор
/// (придуманный <c>factoryId</c>, гигантские числа в <c>amount</c>/<c>count</c>), что бьёт по
/// парсингу (переполнение <see langword="int"/>) чаще, чем просто отсутствие поля.
/// Отдельно: с reasoning-моделью (<c>qwen3.8-27b-mlx</c>, живая проверка 2026-08-16) LM Studio
/// иногда кладёт весь ответ, включая наш JSON, в поле <c>reasoning_content</c> сообщения, а
/// <c>content</c> оставляет пустым — даже при <c>finish_reason: "stop"</c> (не обрезка по лимиту
/// токенов, так отработал шаблон чата модели). См. <see cref="ExtractMessageContent"/> — откат на
/// <c>reasoning_content</c>, когда <c>content</c> пуст.
/// <para>
/// Запрос идёт потоково (<c>stream: true</c>, запрос пользователя 2026-08-16: реальные ходы на
/// медленном сервере занимали от ~4 до ~17 минут, и без streaming нечем отличить «модель думает» от
/// «всё зависло»). <see cref="LmStudioClient(HttpClient, string, double, int, Action{int}?, Action{TimeSpan}?, bool)"/>
/// принимает необязательные <c>onToken</c> (счётчик кусков ответа по мере получения — не точное
/// число токенов, SSE-чанк лленка не всегда ровно один токен, но для «идёт ли ещё генерация» этого
/// достаточно) и <c>onStalled</c> (сколько времени нет новых кусков подряд — консоль показывает
/// предупреждение, не обрывает запрос: возможно, модель просто долго думает над одним трудным
/// токеном, не обязательно зависла).
/// </para>
/// <para>
/// <c>disableThinking</c> (запрос пользователя 2026-08-16: reasoning жрёт токены и сильно замедляет
/// каждый ход) шлёт <c>reasoning_effort: "none"</c>. Это не первый вариант, который проверялся:
/// документированный для Qwen3 способ <c>chat_template_kwargs: { enable_thinking: false }</c> живьём
/// проверен 2026-08-16 против этого же сервера (LM Studio + <c>qwen/qwen3.8-27b</c>) и не сработал —
/// <c>reasoning_content</c> и токены на размышление остались теми же, что и без него (известный баг
/// LM Studio с гибридными Qwen3-моделями). Живая проверка того же дня подтвердила, что
/// <c>reasoning_effort: "none"</c> реально убирает <c>reasoning_content</c> (пусто, 0
/// reasoning-токенов) — и в потоковом, и в обычном режиме, и не ломает не-reasoning модель
/// (<c>gemma-2-9b-it</c>: тот же пустой <c>reasoning_content</c>, без ошибок). Если когда-нибудь
/// окажется, что на другом сервере/версии LM Studio этот параметр тоже перестал помогать —
/// перепроверяйте это живым запросом, а не полагайтесь на доки; так уже подвела одна попытка.
/// </para>
/// </summary>
public sealed class LmStudioClient : ILlmClient
{
    private static readonly TimeSpan StallCheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Адрес LM Studio — из переменной окружения <c>LM_STUDIO_BASE_URL</c>, если задана (запрос
    /// пользователя 2026-08-16: переключаться между ноутбуком и стационарным ПК в сети без правки
    /// кода), иначе локальный запуск по умолчанию.
    /// </summary>
    public static string DefaultBaseUrl =>
        Environment.GetEnvironmentVariable("LM_STUDIO_BASE_URL") is { Length: > 0 } fromEnv
            ? fromEnv
            : "http://localhost:1234/v1/";

    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly double _temperature;
    private readonly int _maxTokens;
    private readonly bool _disableThinking;
    private readonly Action<int>? _onToken;
    private readonly Action<TimeSpan>? _onStalled;

    /// <summary>
    /// <paramref name="httpClient"/> должен иметь выставленный <see cref="HttpClient.BaseAddress"/>
    /// (например, <see cref="DefaultBaseUrl"/>) — сам клиент не создаёт и не владеет
    /// <see cref="HttpClient"/>, чтобы вызывающая сторона управляла его временем жизни
    /// (<c>IHttpClientFactory</c> в реальном раннере, поддельный обработчик в тестах).
    /// <paramref name="disableThinking"/> — см. doc-comment класса (<c>chat_template_kwargs</c>).
    /// <paramref name="onToken"/>/<paramref name="onStalled"/> — см. doc-comment класса; вызываются
    /// синхронно из потока чтения ответа, не должны блокировать надолго.
    /// </summary>
    public LmStudioClient(
        HttpClient httpClient, string model, double temperature = 0.2, int maxTokens = 500,
        Action<int>? onToken = null, Action<TimeSpan>? onStalled = null, bool disableThinking = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model id must not be empty.", nameof(model));
        }

        _httpClient = httpClient;
        _model = model;
        _temperature = temperature;
        _maxTokens = maxTokens;
        _disableThinking = disableThinking;
        _onToken = onToken;
        _onStalled = onStalled;
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        ArgumentNullException.ThrowIfNull(userPrompt);

        var requestBody = BuildRequestBody(systemPrompt, userPrompt);
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        // ResponseHeadersRead — не ждём всё тело целиком, читаем SSE-поток по мере поступления.
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"LM Studio request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {errorText}");
        }

        return await ReadStreamAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReadStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        var chunkCount = 0;
        var lastChunkAt = DateTimeOffset.UtcNow;
        Task<string?>? pendingLine = null;

        while (true)
        {
            pendingLine ??= reader.ReadLineAsync(cancellationToken).AsTask();
            var delay = Task.Delay(StallCheckInterval, cancellationToken);
            var finished = await Task.WhenAny(pendingLine, delay).ConfigureAwait(false);

            if (finished != pendingLine)
            {
                // Строка ещё не пришла — тот же pendingLine продолжает читаться, просто сообщаем о
                // застое и опрашиваем снова, не переоткрывая чтение потока.
                _onStalled?.Invoke(DateTimeOffset.UtcNow - lastChunkAt);
                continue;
            }

            var line = await pendingLine.ConfigureAwait(false);
            pendingLine = null;

            if (line is null)
            {
                break; // поток закрылся без явного [DONE] — считаем, что ответ окончен
            }
            if (line.Length == 0 || !line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue; // SSE: пустые строки-разделители и любые другие поля события не наши
            }

            var payload = line["data: ".Length..];
            if (payload == "[DONE]")
            {
                break;
            }

            using var chunk = JsonDocument.Parse(payload);
            if (!chunk.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                continue;
            }

            var delta = choices[0].GetProperty("delta");
            var appended = false;
            if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } cText)
            {
                content.Append(cText);
                appended = true;
            }
            if (delta.TryGetProperty("reasoning_content", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } rText)
            {
                reasoning.Append(rText);
                appended = true;
            }

            if (appended)
            {
                chunkCount++;
                lastChunkAt = DateTimeOffset.UtcNow;
                _onToken?.Invoke(chunkCount);
            }
        }

        if (content.Length > 0)
        {
            return content.ToString();
        }

        // Тот же откат, что и раньше для нестримингового ответа (doc-comment класса) — reasoning-
        // модель может отдать весь ответ через reasoning_content, оставив content пустым.
        if (reasoning.Length > 0)
        {
            return reasoning.ToString();
        }

        throw new InvalidOperationException("LM Studio streamed response had no content or reasoning_content.");
    }

    private JsonObject BuildRequestBody(string systemPrompt, string userPrompt)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }),
            ["temperature"] = _temperature,
            ["max_tokens"] = _maxTokens,
            ["stream"] = true,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "bot_command_batch",
                    ["strict"] = false,
                    ["schema"] = BotCommandSchema.BuildBatch(),
                },
            },
        };

        if (_disableThinking)
        {
            // См. doc-comment класса: живьём проверенный способ (2026-08-16, LM Studio +
            // qwen/qwen3.8-27b) — chat_template_kwargs.enable_thinking не сработал на этом сервере,
            // reasoning_effort: "none" сработал.
            body["reasoning_effort"] = "none";
        }

        return body;
    }
}
