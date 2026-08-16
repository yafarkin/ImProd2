namespace Game.Bots.Llm.Tests;

/// <summary>
/// Тестовый дублёр <see cref="ILlmClient"/> — отдаёт заранее заданную очередь ответов вместо
/// реального инференса; ровно то, чем шаг 1 плана LLM-ботов доказывает цикл
/// execute→validate→retry без подключения LM Studio/Ollama. Также запоминает промпты, которыми его
/// вызвали, — тесты этим проверяют, что текст ошибки действительно доходит до повторного запроса.
/// </summary>
internal sealed class ScriptedLlmClient : ILlmClient
{
    private readonly Queue<Func<string>> _responses = new();

    public ScriptedLlmClient(params string[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(() => response);
        }
    }

    public List<string> ReceivedUserPrompts { get; } = new();

    /// <summary>Ставит в очередь исключение вместо ответа — для проверки устойчивости <see cref="LlmBotDecisionLoop"/> к сетевым/HTTP-ошибкам клиента (живая проверка 2026-08-16).</summary>
    public void EnqueueException(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
    }

    /// <summary>Ставит в очередь обычный ответ после конструктора — вперемешку с <see cref="EnqueueException"/>, чтобы собрать сценарий «сбой, потом восстановление».</summary>
    public void EnqueueResponse(string response)
    {
        _responses.Enqueue(() => response);
    }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        ReceivedUserPrompts.Add(userPrompt);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("ScriptedLlmClient ran out of scripted responses.");
        }

        return Task.FromResult(_responses.Dequeue()());
    }
}
