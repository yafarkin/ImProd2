namespace Game.Bots.Llm;

/// <summary>
/// Тип команды, которую может вернуть LLM-бот на своём ходу — по одной команде на ответ модели,
/// не пакет команд (§ обоснование в <see cref="BotCommand"/>).
/// </summary>
public enum BotCommandKind
{
    /// <summary>Бот сознательно ничего не делает в этот ход.</summary>
    Nop,

    /// <summary>См. <see cref="Game.Engine.GameSession.BuildFactory"/>.</summary>
    BuildFactory,

    /// <summary>См. <see cref="Game.Engine.GameSession.SetWorkerCount"/>.</summary>
    SetWorkerCount,

    /// <summary>См. <see cref="Game.Engine.GameSession.SelectRecipe"/>.</summary>
    SelectRecipe,

    /// <summary>См. <see cref="Game.Engine.GameSession.SetRndCommitment"/>.</summary>
    SetRndCommitment,

    /// <summary>См. <see cref="Game.Engine.GameSession.SetGenerationResearchCommitment"/>.</summary>
    SetGenerationResearchCommitment,

    /// <summary>См. <see cref="Game.Engine.GameSession.SetOverhaulRequested"/>.</summary>
    SetOverhaulRequested,

    /// <summary>См. <see cref="Game.Engine.GameSession.TakeLoan"/>.</summary>
    TakeLoan,

    /// <summary>См. <see cref="Game.Engine.GameSession.RepayLoan"/>.</summary>
    RepayLoan,

    /// <summary>См. <see cref="Game.Engine.GameSession.SellToSystem"/>.</summary>
    SellToSystem,

    /// <summary>См. <see cref="Game.Engine.GameSession.SellFactory"/>.</summary>
    SellFactory,

    /// <summary>См. <see cref="Game.Engine.GameSession.SetFactoryAllocationShare"/>.</summary>
    SetFactoryAllocationShare,

    /// <summary>См. <see cref="Game.Engine.GameSession.PostNeed"/>.</summary>
    PostNeed,

    /// <summary>См. <see cref="Game.Engine.GameSession.WithdrawNeed"/>.</summary>
    WithdrawNeed,

    /// <summary>См. <see cref="Game.Engine.GameSession.EmergencyPurchase"/>.</summary>
    EmergencyPurchase,
}

/// <summary>
/// Одна команда от LLM-бота — ответ модели за один ход разбирается ровно в один экземпляр этого
/// типа (шаг 1 плана LLM-ботов, docs/TODO.md #20). Форма намеренно плоская, а не дискриминированное
/// объединение по <see cref="Kind"/>: маленьким локальным моделям (4B) проще стабильно вернуть один
/// JSON-объект с необязательными полями, чем строго переключаемую по тегу схему; поля, не
/// относящиеся к данному <see cref="Kind"/>, <see cref="BotCommandExecutor"/> просто игнорирует.
/// </summary>
public sealed record BotCommand
{
    /// <summary>Какое действие запрашивается.</summary>
    public required BotCommandKind Kind { get; init; }

    /// <summary>Идентификатор типа фабрики из каталога — для <see cref="BotCommandKind.BuildFactory"/>.</summary>
    public string? FactoryDefinitionId { get; init; }

    /// <summary>
    /// Идентификатор уже построенной фабрики команды — для команд, действующих на конкретную
    /// фабрику, включая <see cref="BotCommandKind.SellFactory"/> (продажа/ликвидация целиком) и
    /// <see cref="BotCommandKind.SetFactoryAllocationShare"/>.
    /// </summary>
    public Ulid? FactoryId { get; init; }

    /// <summary>
    /// Идентификатор рецепта — необязателен для <see cref="BotCommandKind.BuildFactory"/> (по
    /// умолчанию первый рецепт фабрики, как и в <see cref="Game.Engine.GameSession.BuildFactory"/>),
    /// обязателен для <see cref="BotCommandKind.SelectRecipe"/>.
    /// </summary>
    public string? RecipeId { get; init; }

    /// <summary>Денежная сумма — для займа, погашения, R&amp;D и исследования поколения.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Число рабочих — для <see cref="BotCommandKind.SetWorkerCount"/>.</summary>
    public int? Count { get; init; }

    /// <summary>Идентификатор материала — для <see cref="BotCommandKind.SellToSystem"/>, <see cref="BotCommandKind.EmergencyPurchase"/> и <see cref="BotCommandKind.PostNeed"/>.</summary>
    public string? MaterialId { get; init; }

    /// <summary>Объём материала — для <see cref="BotCommandKind.SellToSystem"/> и <see cref="BotCommandKind.EmergencyPurchase"/>.</summary>
    public decimal? Volume { get; init; }

    /// <summary>Включить/выключить запрос — для <see cref="BotCommandKind.SetOverhaulRequested"/>.</summary>
    public bool? Enabled { get; init; }

    /// <summary>Вес доли при разборе дефицитного сырья между своими фабриками — для <see cref="BotCommandKind.SetFactoryAllocationShare"/>.</summary>
    public decimal? Share { get; init; }

    /// <summary>
    /// «surplus» (избыток, есть чем поделиться) или «deficit» (дефицит, команда ищет материал) —
    /// для <see cref="BotCommandKind.PostNeed"/>.
    /// </summary>
    public string? Direction { get; init; }

    /// <summary>Грубый порядок объёма — «small», «medium» или «large» — для <see cref="BotCommandKind.PostNeed"/>.</summary>
    public string? VolumeOrder { get; init; }

    /// <summary>Необязательный комментарий к записи на доске потребностей — для <see cref="BotCommandKind.PostNeed"/>.</summary>
    public string? Comment { get; init; }

    /// <summary>Идентификатор записи на доске потребностей — для <see cref="BotCommandKind.WithdrawNeed"/>.</summary>
    public Ulid? NeedId { get; init; }

    /// <summary>
    /// Свободная заметка, которую модель оставляет сама себе, — по запросу пользователя, чтобы бот
    /// мог понимать свои прошлые решения на следующих ходах. Промпт, собирающий эти заметки обратно
    /// в контекст, — отдельный, более поздний шаг плана; здесь поле только проходит насквозь и
    /// попадает в <see cref="BotDecisionLog"/>.
    /// </summary>
    public string? Annotation { get; init; }
}
