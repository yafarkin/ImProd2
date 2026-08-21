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

    /// <summary>Публикует заявку на продажу материала. См. <see cref="Game.Engine.GameSession.PostTradeOffer"/>.</summary>
    PostSellOffer,

    /// <summary>Публикует заявку на покупку материала. См. <see cref="Game.Engine.GameSession.PostTradeOffer"/>.</summary>
    PostBuyOffer,

    /// <summary>См. <see cref="Game.Engine.GameSession.WithdrawTradeOffer"/>.</summary>
    WithdrawTradeOffer,

    /// <summary>Исполняет чужую заявку с доски публичных заявок. См. <see cref="Game.Engine.GameSession.MarkTradeOfferFulfilled"/>.</summary>
    FulfillTradeOffer,
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

    /// <summary>Денежная сумма — для R&amp;D и исследования поколения.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Число рабочих — для <see cref="BotCommandKind.SetWorkerCount"/>.</summary>
    public int? Count { get; init; }

    /// <summary>
    /// Идентификатор материала — для <see cref="BotCommandKind.SellToSystem"/>, <see cref="BotCommandKind.EmergencyPurchase"/>,
    /// <see cref="BotCommandKind.PostNeed"/>, <see cref="BotCommandKind.PostSellOffer"/> и <see cref="BotCommandKind.PostBuyOffer"/>.
    /// </summary>
    public string? MaterialId { get; init; }

    /// <summary>
    /// Объём материала — для <see cref="BotCommandKind.SellToSystem"/>, <see cref="BotCommandKind.EmergencyPurchase"/>,
    /// <see cref="BotCommandKind.PostSellOffer"/>/<see cref="BotCommandKind.PostBuyOffer"/> (за одну поставку) и
    /// <see cref="BotCommandKind.FulfillTradeOffer"/> (сколько из заявки исполняется).
    /// </summary>
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
    /// <see langword="true"/> — регулярная заявка (поставка каждый ход, пока не исполнят или не
    /// отзовут), <see langword="false"/>/не задано — разовая. Для <see cref="BotCommandKind.PostSellOffer"/>
    /// и <see cref="BotCommandKind.PostBuyOffer"/>.
    /// </summary>
    public bool? Recurring { get; init; }

    /// <summary>
    /// Минимально приемлемая цена за единицу — публикующая сторона называет вилку вместо торга
    /// (у ботов его не будет). Для <see cref="BotCommandKind.PostSellOffer"/> и <see cref="BotCommandKind.PostBuyOffer"/>.
    /// </summary>
    public decimal? MinPrice { get; init; }

    /// <summary>Максимально приемлемая цена за единицу. Для <see cref="BotCommandKind.PostSellOffer"/> и <see cref="BotCommandKind.PostBuyOffer"/>.</summary>
    public decimal? MaxPrice { get; init; }

    /// <summary>Идентификатор заявки с доски публичных заявок — для <see cref="BotCommandKind.WithdrawTradeOffer"/> и <see cref="BotCommandKind.FulfillTradeOffer"/>.</summary>
    public Ulid? TradeOfferId { get; init; }

    /// <summary>
    /// Точная цена за единицу, в пределах диапазона исполняемой заявки — для <see cref="BotCommandKind.FulfillTradeOffer"/>.
    /// </summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>
    /// Почему модель выбрала именно это действие прямо сейчас, исходя из текущего состояния —
    /// запрос пользователя 2026-08-19: «попросим модель объяснять каждое своё действие». В отличие
    /// от <see cref="Annotation"/> — обязательное (см. <c>required</c> в <see cref="BotCommandSchema"/>)
    /// и НЕ переносится в <see cref="BotTurnHistory"/> на будущие ходы (см. doc-comment
    /// <see cref="BotCommandSummary.Describe"/>) — попадает только в <see cref="BotDecisionLog"/>,
    /// для разбора «почему бот так решил» человеком, не для памяти самого бота. Не накапливается
    /// из хода в ход, поэтому не растит промпт со временем, в отличие от аннотации.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Свободная НЕОБЯЗАТЕЛЬНАЯ заметка, которую модель оставляет сама себе на будущее — по запросу
    /// пользователя, чтобы бот мог понимать свои прошлые решения на следующих ходах. В отличие от
    /// <see cref="Reason"/> (обязательное объяснение действия «здесь и сейчас», не сохраняется) —
    /// именно это поле попадает в <see cref="BotTurnHistory"/> и накапливается в промпте из хода в
    /// ход, поэтому и осталось необязательным и коротким по правилам промпта.
    /// </summary>
    public string? Annotation { get; init; }
}
