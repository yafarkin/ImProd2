using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots;

/// <summary>Какое действие запрашивает человек, ведущий команду вручную — см. <see cref="InteractiveSessionRunner"/>.</summary>
public enum ManualActionKind
{
    /// <summary>См. <see cref="GameSession.BuildFactory"/>.</summary>
    BuildFactory,

    /// <summary>См. <see cref="GameSession.SetWorkerCount"/>.</summary>
    SetWorkerCount,

    /// <summary>См. <see cref="GameSession.SelectRecipe"/>.</summary>
    SelectRecipe,

    /// <summary>См. <see cref="GameSession.SetRndCommitment"/>.</summary>
    SetRndCommitment,

    /// <summary>См. <see cref="GameSession.SetGenerationResearchCommitment"/>.</summary>
    SetGenerationResearchCommitment,

    /// <summary>См. <see cref="GameSession.SetOverhaulRequested"/>.</summary>
    SetOverhaulRequested,

    /// <summary>См. <see cref="GameSession.SellToSystem"/>.</summary>
    SellToSystem,

    /// <summary>См. <see cref="GameSession.EmergencyPurchase"/>.</summary>
    EmergencyPurchase,
}

/// <summary>
/// Одно действие человека за ручную команду в <see cref="InteractiveSessionRunner"/> — плоская форма
/// (поля не относящиеся к <see cref="Kind"/> просто не используются), тот же приём, что и
/// <c>Game.Bots.Llm.BotCommand</c> у LLM-бота, здесь заведено заново и меньшим набором полей: та
/// команда обслуживает JSON-схему для модели и P2P-доску заявок, которых этому инструменту не нужно
/// (см. doc-comment класса), тянуть отдельный проект ради общего типа было бы дороже, чем эти ~15 строк.
/// Поля (кроме <see cref="Kind"/>) — <c>set</c>, не <c>init</c>: UI редактирует значения предложения
/// бота (<see cref="InteractiveSessionRunner.ProposeManualTeamDecision"/>) прямо на месте, привязкой
/// `@bind`, до того как пользователь решит применить действие или пропустить его.
/// </summary>
public sealed record ManualAction
{
    public required ManualActionKind Kind { get; init; }

    /// <summary>Тип фабрики из каталога — для <see cref="ManualActionKind.BuildFactory"/>.</summary>
    public string? FactoryDefinitionId { get; set; }

    /// <summary>
    /// Уже построенная фабрика команды — для всех действий, нацеленных на конкретную фабрику.
    /// Особый случай — <see cref="ManualActionKind.BuildFactory"/> внутри предложения бота (см.
    /// <see cref="InteractiveSessionRunner.ProposeManualTeamDecision"/>): там это поле не факт
    /// (постройка ещё не совершена), а служебный идентификатор фабрики В ЧЕРНОВОЙ КОПИИ сессии, по
    /// которому <see cref="InteractiveSessionRunner.ApplyProposedActions"/> сопоставляет эту постройку
    /// с другими действиями того же предложения, нацеленными на ту же новую фабрику (наём, R&amp;D) —
    /// у настоящей сессии при реальной постройке фабрика получит другой, новый Id. Для обычного
    /// <see cref="ManualActionKind.BuildFactory"/>, введённого человеком через форму, всегда <see
    /// langword="null"/> и ни на что не влияет (<see cref="InteractiveSessionRunner.ApplyManualAction"/>
    /// это поле для данного вида действия не читает).
    /// </summary>
    public Ulid? FactoryId { get; set; }

    /// <summary>Рецепт — необязателен для <see cref="ManualActionKind.BuildFactory"/>, обязателен для <see cref="ManualActionKind.SelectRecipe"/>.</summary>
    public string? RecipeId { get; set; }

    /// <summary>Денежная сумма — для R&amp;D и исследования поколения.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Число рабочих — для <see cref="ManualActionKind.SetWorkerCount"/>.</summary>
    public int? Count { get; set; }

    /// <summary>Материал — для <see cref="ManualActionKind.SellToSystem"/>/<see cref="ManualActionKind.EmergencyPurchase"/>.</summary>
    public string? MaterialId { get; set; }

    /// <summary>Объём — для <see cref="ManualActionKind.SellToSystem"/>/<see cref="ManualActionKind.EmergencyPurchase"/>.</summary>
    public decimal? Volume { get; set; }

    /// <summary>Включить/выключить — для <see cref="ManualActionKind.SetOverhaulRequested"/>.</summary>
    public bool? Enabled { get; set; }
}

/// <summary>Итог применения одного <see cref="ManualAction"/> к сессии.</summary>
public abstract record ManualActionResult
{
    private ManualActionResult()
    {
    }

    /// <summary>Действие применено, <see cref="Description"/> — короткая строка для журнала на экране.</summary>
    public sealed record Success(string Description) : ManualActionResult;

    /// <summary>Не хватило полей или сама <see cref="GameSession"/> отказала (её обычные <see cref="ArgumentException"/>/<see cref="InvalidOperationException"/>).</summary>
    public sealed record Failure(string Message) : ManualActionResult;
}

/// <summary>
/// Пошаговый прогон партии, где одной командой управляет живой человек через
/// <see cref="ApplyManualAction"/> вместо <see cref="SimpleBot"/> — продолжение «отладка
/// производства» (docs/rebalance-2sector/balance-experiment-plan.md), прямой запрос пользователя
/// 2026-08-24: «не только посмотреть какие действия выбрал бот, но и задать их самому», чтобы вживую
/// проверять гипотезы о поведении бота (например, гипотезу удержания буфера вместо немедленной
/// продажи системе из раздела «отладка производства»), а не только читать текст решений бота
/// постфактум.
/// <para>
/// Остальные секторы, как и в <see cref="DetailedSessionRunner"/>, по-прежнему ведёт
/// <see cref="SimpleBot"/> (по одной команде на сектор) — партия не превращается в одиночный тест,
/// ручная команда действует на фоне того же рыночного давления/дефицита сырья, что и в обычном
/// прогоне ботов.
/// </para>
/// <para>
/// Набор действий — 6 из 7 категорий решений <see cref="SimpleBot"/> (см.
/// <see cref="DetailedSessionRunner.TurnDecisionLog"/>): постройка, наём, рецепт, темп R&amp;D/
/// поколения, капремонт, продажа системе, аварийная закупка. P2P-обмен (заявки на доску, стакан
/// сделок) сознательно не включён в первую версию — вопрос, ради которого строился инструмент,
/// касается момента продажи системе, не переговоров между командами; расширяется теми же методами
/// <see cref="GameSession"/>, что уже использует <c>Team.razor</c>, если понадобится позже.
/// </para>
/// <para>
/// <see cref="ProposeManualTeamDecision"/> (запрос пользователя 2026-08-24: «хочу видеть предложенные
/// действия бота и обоснование, но опционально менять их или не менять вообще») считает, что сделал
/// бы <see cref="SimpleBot"/> для РУЧНОЙ команды на этот ход — тем же самым кодом, что ведёт остальные
/// секторы, значит и обоснование в точности бот-текст, не приближение. Считается на ЧЕРНОВОЙ копии
/// сессии (<see cref="CloneSession"/> — реплей той же цепочки событий на свежий <see
/// cref="GameSessionState"/>, тот же приём, что и восстановление <c>DurableEventLog</c> после сбоя),
/// не на настоящей: постройка фабрики — единственное из семи решений, которое тратит деньги
/// немедленно и по-настоящему необратимо (остальные шесть — декларации на ближайший расчёт, свободно
/// перезаписываемые хоть каждый ход, поэтому применять их сразу было бы так же безопасно, как и
/// после подтверждения — но раз пользователь просил видеть предложение целиком до применения, черновик
/// строится один раз для всех семи, а не только для постройки, ради единообразия UI).
/// </para>
/// </summary>
public sealed class InteractiveSessionRunner
{
    /// <summary>Итог одного полного хода — то, что бот(ы) сделали за ход, и что получилось у ручной команды.</summary>
    public sealed record TurnResult(
        int Turn,
        IReadOnlyList<string> BotTraceLines,
        DetailedSessionRunner.TeamTurnSnapshot ManualTeamSnapshot,
        bool IsFinished);

    public GameSession Session { get; }
    public Ulid ManualTeamId { get; }

    private readonly List<SimpleBot> _bots;
    private readonly Random _random;
    private readonly List<string> _currentTurnTraceLines;
    private readonly IReadOnlyDictionary<string, decimal> _materialCosts;
    private readonly Dictionary<Ulid, decimal> _previousNetByTeam = new();
    private readonly bool _maintainFactories;
    private readonly decimal _leverage;
    private readonly decimal _profile;
    private bool _hasBuiltOut;

    private InteractiveSessionRunner(
        GameSession session, Ulid manualTeamId, List<SimpleBot> bots, Random random,
        IReadOnlyDictionary<string, decimal> materialCosts, List<string> traceLines,
        bool maintainFactories, decimal leverage, decimal profile)
    {
        Session = session;
        ManualTeamId = manualTeamId;
        _bots = bots;
        _random = random;
        _materialCosts = materialCosts;
        _currentTurnTraceLines = traceLines;
        _maintainFactories = maintainFactories;
        _leverage = leverage;
        _profile = profile;
    }

    /// <summary>
    /// Заводит по одной команде на сектор — одну из них (сектор <paramref name="manualSectorId"/>)
    /// человек ведёт вручную, остальные ведёт <see cref="SimpleBot"/> — и сразу проигрывает расчёт
    /// первого хода (сессия открывается в фазе <see cref="TurnPhase.Settlement"/> первого хода, см.
    /// doc-comment <see cref="GameSession.StartWithEndTurn(ResolvedGameConfig,int,IReadOnlyList{TeamSpec},System.Text.Json.JsonSerializerOptions?,System.Func{System.DateTimeOffset}?)"/>)
    /// — тот же первый шаг, что и <see cref="BotSessionRunner.RunToCompletion"/>, до входа в <see
    /// cref="TurnPhase.Decision"/>, чтобы вызывающий сразу получил готовую к решениям сессию.
    /// </summary>
    public static InteractiveSessionRunner Start(
        ResolvedGameConfig config, int endTurn, string manualSectorId,
        bool maintainFactories, decimal leverage, decimal profile)
    {
        ArgumentNullException.ThrowIfNull(config);

        var teamSectorPairs = config.Sectors
            .Select(sector => (Spec: new TeamSpec { Id = Ulid.NewUlid(), Name = $"{sector.Id}-0", SectorId = sector.Id }, Sector: sector))
            .ToList();
        var manualPair = teamSectorPairs.FirstOrDefault(p => p.Sector.Id == manualSectorId);
        if (manualPair.Spec is null)
        {
            throw new ArgumentException($"Unknown sector '{manualSectorId}'.", nameof(manualSectorId));
        }

        var session = GameSession.StartWithEndTurn(config, endTurn, teamSectorPairs.Select(p => p.Spec).ToList());
        var random = new Random(2); // тот же детерминированный посев, что и DetailedSessionRunner/--mode trace
        var traceLines = new List<string>();

        var bots = teamSectorPairs
            .Where(p => p.Spec.Id != manualPair.Spec.Id)
            .Select(p => new SimpleBot(p.Spec.Id, p.Sector, config, maintainFactories, leverage, profile, trace: traceLines.Add))
            .ToList();

        var runner = new InteractiveSessionRunner(
            session, manualPair.Spec.Id, bots, random, MaterialCostCalculator.CalculateAll(config), traceLines,
            maintainFactories, leverage, profile);
        runner.RunInitialSettlement();
        return runner;
    }

    private void RunInitialSettlement()
    {
        if (Session.State.CurrentPhase == TurnPhase.Settlement && !Session.State.IsFinished)
        {
            Session.RunTick(_random);
            Session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }
    }

    /// <summary>
    /// Применяет одно действие ручной команды немедленно — как и у настоящего игрока на
    /// <c>Team.razor</c>, эффект (списание денег на постройку, объявление рабочих и т.п.) виден
    /// сразу, не откладывается до <see cref="CompleteTurn"/>. Можно вызывать сколько угодно раз за
    /// один ход декизии, любые действия можно перевызвать/поменять до <see cref="CompleteTurn"/>.
    /// </summary>
    public ManualActionResult ApplyManualAction(ManualAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return action.Kind switch
            {
                ManualActionKind.BuildFactory => ApplyBuildFactory(action),
                ManualActionKind.SetWorkerCount => ApplySetWorkerCount(action),
                ManualActionKind.SelectRecipe => ApplySelectRecipe(action),
                ManualActionKind.SetRndCommitment => ApplySetRndCommitment(action),
                ManualActionKind.SetGenerationResearchCommitment => ApplySetGenerationResearchCommitment(action),
                ManualActionKind.SetOverhaulRequested => ApplySetOverhaulRequested(action),
                ManualActionKind.SellToSystem => ApplySellToSystem(action),
                ManualActionKind.EmergencyPurchase => ApplyEmergencyPurchase(action),
                _ => new ManualActionResult.Failure($"Unknown action kind '{action.Kind}'."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new ManualActionResult.Failure(ex.Message);
        }
    }

    private ManualActionResult ApplyBuildFactory(ManualAction action)
    {
        if (action.FactoryDefinitionId is null)
        {
            return new ManualActionResult.Failure("Не указан тип фабрики.");
        }

        Session.BuildFactory(ManualTeamId, action.FactoryDefinitionId, action.RecipeId);
        return new ManualActionResult.Success($"Построена фабрика «{action.FactoryDefinitionId}».");
    }

    private ManualActionResult ApplySetWorkerCount(ManualAction action)
    {
        if (action.FactoryId is not { } factoryId || action.Count is not { } count)
        {
            return new ManualActionResult.Failure("Нужны фабрика и число рабочих.");
        }

        Session.SetWorkerCount(ManualTeamId, factoryId, count);
        return new ManualActionResult.Success($"Объявлено рабочих: {count}.");
    }

    private ManualActionResult ApplySelectRecipe(ManualAction action)
    {
        if (action.FactoryId is not { } factoryId || action.RecipeId is null)
        {
            return new ManualActionResult.Failure("Нужны фабрика и рецепт.");
        }

        Session.SelectRecipe(ManualTeamId, factoryId, action.RecipeId);
        return new ManualActionResult.Success($"Выбран рецепт «{action.RecipeId}».");
    }

    private ManualActionResult ApplySetRndCommitment(ManualAction action)
    {
        if (action.FactoryId is not { } factoryId || action.Amount is not { } amount)
        {
            return new ManualActionResult.Failure("Нужны фабрика и сумма.");
        }

        Session.SetRndCommitment(ManualTeamId, factoryId, amount);
        return new ManualActionResult.Success($"R&D фабрики: {amount:0.##}¤/ход.");
    }

    private ManualActionResult ApplySetGenerationResearchCommitment(ManualAction action)
    {
        if (action.Amount is not { } amount)
        {
            return new ManualActionResult.Failure("Нужна сумма.");
        }

        Session.SetGenerationResearchCommitment(ManualTeamId, amount);
        return new ManualActionResult.Success($"Исследование поколения: {amount:0.##}¤/ход.");
    }

    private ManualActionResult ApplySetOverhaulRequested(ManualAction action)
    {
        if (action.FactoryId is not { } factoryId || action.Enabled is not { } enabled)
        {
            return new ManualActionResult.Failure("Нужны фабрика и признак включения.");
        }

        Session.SetOverhaulRequested(ManualTeamId, factoryId, enabled);
        return new ManualActionResult.Success(enabled ? "Капремонт заказан." : "Запрос капремонта отменён.");
    }

    private ManualActionResult ApplySellToSystem(ManualAction action)
    {
        if (action.MaterialId is null || action.Volume is not { } volume)
        {
            return new ManualActionResult.Failure("Нужны материал и объём.");
        }

        Session.SellToSystem(ManualTeamId, action.MaterialId, volume);
        return new ManualActionResult.Success($"Продажа системе: {volume:0.##} «{action.MaterialId}».");
    }

    private ManualActionResult ApplyEmergencyPurchase(ManualAction action)
    {
        if (action.MaterialId is null || action.Volume is not { } volume)
        {
            return new ManualActionResult.Failure("Нужны материал и объём.");
        }

        Session.EmergencyPurchase(ManualTeamId, action.MaterialId, volume);
        return new ManualActionResult.Success($"Аварийная закупка: {volume:0.##} «{action.MaterialId}».");
    }

    /// <summary>Предложение бота на текущий ход декизии для ручной команды — см. <see cref="ProposeManualTeamDecision"/>.</summary>
    public sealed record TurnProposal(DetailedSessionRunner.TurnDecisionLog Reasoning, IReadOnlyList<ManualAction> Actions);

    /// <summary>
    /// Считает, что сделал бы <see cref="SimpleBot"/> для ручной команды на этот ход, ничего не
    /// применяя к настоящей сессии — см. второй doc-comment класса. Гоняет ровно тот же набор методов
    /// бота, что и <see cref="CompleteTurn"/> для остальных секторов (кроме P2P-стакана, вне области
    /// применения этого инструмента), на черновой копии (<see cref="CloneSession"/>), затем разбирает
    /// новые события черновика обратно в <see cref="ManualAction"/> (<see cref="ToManualAction"/>) и
    /// текст обоснования — тем же разборщиком трассировки, что и «Пошаговый просмотр решений бота»
    /// (<see cref="DetailedSessionRunner.BuildDecisionLogs"/>), поэтому обоснование выглядит одинаково
    /// в обоих местах UI.
    /// </summary>
    public TurnProposal ProposeManualTeamDecision()
    {
        Session.EnsureDecisionsAllowed();

        var clone = CloneSession();
        var manualTeam = Session.State.Teams[ManualTeamId];
        var turn = Session.State.CurrentTurn;
        var taggedTraceLines = new List<(int Turn, string Line)>();

        var bot = new SimpleBot(
            ManualTeamId, manualTeam.Sector, Session.State.Config, _maintainFactories, _leverage, _profile,
            trace: line => taggedTraceLines.Add((turn, line)));

        var startIndex = clone.Entries.Count;
        bot.UpdateFinancialTrend(clone);
        bot.BuildNewlyUnlockedFactories(clone);
        bot.UpdateInvestmentPace(clone);
        bot.MaintainFactories(clone);
        bot.SellSurplusToSystem(clone);

        var actions = clone.Entries.Skip(startIndex)
            .Select(entry => ToManualAction(entry.Change))
            .Where(action => action is not null)
            .Select(action => action!)
            .ToList();

        var reasoning = DetailedSessionRunner.BuildDecisionLogs(taggedTraceLines).SingleOrDefault()
            ?? new DetailedSessionRunner.TurnDecisionLog
            {
                Turn = turn,
                SectorId = manualTeam.Sector.Id,
                FinancialTrend = [],
                Build = [],
                InvestmentPace = [],
                Overhaul = [],
                Sell = [],
                Buy = [],
                SystemSale = [],
            };

        return new TurnProposal(reasoning, actions);
    }

    /// <summary>
    /// Применяет к настоящей сессии подмножество (или всё) предложения <see
    /// cref="ProposeManualTeamDecision"/>, выбранное пользователем — по одному <see
    /// cref="ApplyManualAction"/> на действие, В ТОМ ЖЕ ПОРЯДКЕ, что и в предложении (важно: постройка
    /// новой фабрики обязана идти раньше действий над ней самой, наём/R&amp;D). Черновая фабрика
    /// (<see cref="ManualAction.FactoryId"/> у <see cref="ManualActionKind.BuildFactory"/>, см. его
    /// doc-comment) переотображается на настоящий Id, полученный от настоящей постройки, для всех
    /// последующих действий этого же вызова, которые на неё ссылаются — без этого «наём на только что
    /// предложенную, ещё не существующую в настоящей сессии фабрику» бился бы об «неизвестная
    /// фабрика». Действия, не входящие в <paramref name="actions"/> (пользователь снял галочку),
    /// просто не передаются — вызывающий сам решает, что отобрать.
    /// </summary>
    public IReadOnlyList<ManualActionResult> ApplyProposedActions(IReadOnlyList<ManualAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var draftToRealFactoryId = new Dictionary<Ulid, Ulid>();
        var results = new List<ManualActionResult>(actions.Count);

        foreach (var action in actions)
        {
            var effective = action.Kind != ManualActionKind.BuildFactory
                && action.FactoryId is { } draftFactoryId
                && draftToRealFactoryId.TryGetValue(draftFactoryId, out var realFactoryId)
                    ? action with { FactoryId = realFactoryId }
                    : action;

            var result = ApplyManualAction(effective);
            results.Add(result);

            if (action is { Kind: ManualActionKind.BuildFactory, FactoryId: { } draftBuiltId } && result is ManualActionResult.Success)
            {
                var justBuilt = Session.State.Teams[ManualTeamId].Factories[^1];
                draftToRealFactoryId[draftBuiltId] = justBuilt.Id;
            }
        }

        return results;
    }

    /// <summary>Переводит одно новое событие черновой сессии обратно в <see cref="ManualAction"/> — обратная сторона диспетчеризации <see cref="ApplyManualAction"/>. <see langword="null"/> — событие не относится ни к одной из 7 категорий (в проверенном наборе методов такого не бывает, но на случай будущего расширения <see cref="SimpleBot"/> лучше молча пропустить, чем упасть).</summary>
    private static ManualAction? ToManualAction(Change<GameSessionState> change) => change switch
    {
        FactoryBuilt c => new ManualAction { Kind = ManualActionKind.BuildFactory, FactoryDefinitionId = c.FactoryDefinitionId, RecipeId = c.RecipeId, FactoryId = c.FactoryId },
        WorkerCountSet c => new ManualAction { Kind = ManualActionKind.SetWorkerCount, FactoryId = c.FactoryId, Count = c.Count },
        RecipeSelected c => new ManualAction { Kind = ManualActionKind.SelectRecipe, FactoryId = c.FactoryId, RecipeId = c.RecipeId },
        RndCommitmentSet c => new ManualAction { Kind = ManualActionKind.SetRndCommitment, FactoryId = c.FactoryId, Amount = c.Amount },
        FactoryOverhaulRequestSet c => new ManualAction { Kind = ManualActionKind.SetOverhaulRequested, FactoryId = c.FactoryId, Enabled = c.Requested },
        GenerationResearchCommitmentSet c => new ManualAction { Kind = ManualActionKind.SetGenerationResearchCommitment, Amount = c.Amount },
        MaterialSaleRequested c => new ManualAction { Kind = ManualActionKind.SellToSystem, MaterialId = c.MaterialId, Volume = c.Volume },
        _ => null,
    };

    /// <summary>
    /// Черновая копия настоящей сессии — свежий <see cref="GameSessionState"/> плюс реплей ТЕХ ЖЕ
    /// событий (<c>Change.Apply</c>), тот же приём, что и восстановление <c>DurableEventLog</c> после
    /// сбоя (Game.Persistence). Детерминированно и без побочных эффектов на настоящую сессию — каждый
    /// <see cref="Change{TState}"/> уже содержит готовый исход любого решения, принятого при первой
    /// записи (включая случайность, например выбор новости), реплей просто применяет его заново к
    /// независимому объекту состояния. Использовать только для «что бы сделал бот» — черновик
    /// выбрасывается сразу после чтения, никогда не подменяет <see cref="Session"/>.
    /// </summary>
    private GameSession CloneSession()
    {
        var draftState = new GameSessionState(Session.State.Config);
        foreach (var entry in Session.Entries)
        {
            entry.Change.Apply(draftState);
        }

        var draftLog = new EventLog<GameSessionState>(draftState, Session.Entries);
        return new GameSession(draftLog);
    }

    /// <summary>
    /// Завершает текущий ход декизии: боты всех остальных секторов принимают решения (то же самое,
    /// что и один проход тела цикла <see cref="TurnPhase.Decision"/> в <see
    /// cref="BotSessionRunner.RunToCompletion"/>, здесь развёрнуто вручную, поскольку та реализация
    /// не даёт остановиться посреди хода), затем считается расчёт следующего хода (если партия ещё не
    /// закончилась) — вызывающий получает сессию снова готовой к решениям, на ход дальше.
    /// </summary>
    public TurnResult CompleteTurn()
    {
        if (Session.State.IsFinished)
        {
            throw new InvalidOperationException("Партия уже завершена.");
        }
        Session.EnsureDecisionsAllowed();

        _currentTurnTraceLines.Clear();

        if (!_hasBuiltOut)
        {
            foreach (var bot in _bots)
            {
                bot.BuildOutSectorChain(Session);
            }
            _hasBuiltOut = true;
        }

        foreach (var bot in _bots)
        {
            bot.UpdateFinancialTrend(Session);
            bot.BuildNewlyUnlockedFactories(Session);
            bot.UpdateInvestmentPace(Session);
            bot.MaintainFactories(Session);
        }

        var sellOrders = _bots.SelectMany(bot => bot.ComputeSellOrders(Session)).ToList();
        var buyOrders = _bots.SelectMany(bot => bot.ComputeBuyOrders(Session)).ToList();
        OrderBook.Match(Session, sellOrders, buyOrders, _random);

        foreach (var bot in _bots)
        {
            bot.SellSurplusToSystem(Session);
        }

        // Снимок ручной команды — здесь, а не после расчёта следующего хода: тот же момент, что и
        // onTurnCompleted у DetailedSessionRunner/BotSessionRunner (после решений хода N, до расчёта
        // хода N+1), иначе кумулятивные суммы (BuildSnapshot читает весь журнал заново) захватят и
        // расходы уже следующего хода.
        var completedTurn = Session.State.CurrentTurn;
        var traceThisTurn = _currentTurnTraceLines.ToList();
        var manualTeam = Session.State.Teams[ManualTeamId];
        var snapshot = DetailedSessionRunner.BuildSnapshot(Session, manualTeam, _materialCosts, _previousNetByTeam);

        Session.AdvancePhase(PhaseTransitionTrigger.Timer);
        if (!Session.State.IsFinished)
        {
            Session.RunTick(_random);
            Session.AdvancePhase(PhaseTransitionTrigger.Timer);
        }

        return new TurnResult(completedTurn, traceThisTurn, snapshot, Session.State.IsFinished);
    }
}
