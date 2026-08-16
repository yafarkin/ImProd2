namespace Game.Bots.Llm;

/// <summary>
/// Ответ модели на ЦЕЛЫЙ ход разом — один вызов <see cref="ILlmClient"/> за ход (запрос пользователя
/// 2026-08-16: «только раз за ход обращаться к LLM, и чтобы он сразу формировал массив команд на
/// ход» — раньше каждое действие хода было отдельным вызовом, см. историю <see cref="LlmBotDecisionLoop"/>).
/// <see cref="Actions"/> — ноль и более команд в том порядке, в котором их нужно исполнить; пустой
/// список означает «в этот ход делать нечего» (аналог прежнего отдельного kind=nop-ответа).
/// </summary>
public sealed record BotCommandBatch
{
    public required IReadOnlyList<BotCommand> Actions { get; init; }
}
