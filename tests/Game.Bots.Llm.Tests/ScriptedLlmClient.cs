namespace Game.Bots.Llm.Tests;

/// <summary>
/// Тестовый дублёр <see cref="ILlmClient"/> — отдаёт заранее заданную очередь ответов вместо
/// реального инференса; ровно то, чем шаг 1 плана LLM-ботов доказывает цикл
/// execute→validate→retry без подключения LM Studio/Ollama. Также запоминает промпты, которыми его
/// вызвали, — тесты этим проверяют, что текст ошибки действительно доходит до повторного запроса.
/// </summary>
internal sealed class ScriptedLlmClient : ILlmClient
{
    private readonly Queue<string> _responses;

    public ScriptedLlmClient(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    public List<string> ReceivedUserPrompts { get; } = new();

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        ReceivedUserPrompts.Add(userPrompt);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("ScriptedLlmClient ran out of scripted responses.");
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
