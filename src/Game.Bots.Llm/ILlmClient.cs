namespace Game.Bots.Llm;

/// <summary>
/// Контракт клиента локальной LLM. LM Studio и Ollama оба говорят OpenAI-совместимым
/// <c>/v1/chat/completions</c> (шаги 2-3 плана LLM-ботов, docs/TODO.md #20), поэтому один интерфейс
/// накрывает оба бэкенда без различий на этом уровне — HTTP-реализация появится позже; на шаге 1 есть
/// только контракт и тестовый дублёр со сценарием заранее заданных ответов.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Один запрос — один ответ, без накопленного контекста между вызовами (решение пользователя:
    /// не вести переписку). Вся история и текущее состояние собираются заново в
    /// <paramref name="userPrompt"/> каждый раз вызывающей стороной.
    /// </summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
