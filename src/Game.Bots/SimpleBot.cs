using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Простая ботовая стратегия команды (Блок 7.1, BUILD_PLAN «Фаза 7»): полная вертикальная
/// интеграция внутри своего сектора — строит все типы фабрик сектора и нанимает базовую
/// численность рабочих один раз на первом ходу (SPEC §5.6), затем каждый ход продаёт системе
/// излишек финального продукта сектора сверх уже законтрактованного объёма (SPEC §5.4). Простой
/// spot-контракт с напарником по сектору (SPEC §6) заводится отдельно, статическим методом
/// <see cref="TrySignSimpleContract"/>, — сам бот не ведёт переговоры, только строит, нанимает и
/// продаёт; согласование пары ботов на контракт — забота вызывающего кода (<see cref="BotSessionRunner"/>).
/// </summary>
public sealed class SimpleBot
{
    private const int ContractIntervalTurns = 4;
    private const decimal ContractVolume = 5m;

    /// <summary>Команда, за которую действует бот.</summary>
    public Ulid TeamId { get; }

    /// <summary>Сектор команды.</summary>
    public Sector Sector { get; }

    private readonly IReadOnlyList<FactoryDefinition> _sectorFactories;
    private readonly bool _maintainsFactories;

    /// <summary>
    /// <paramref name="maintainsFactories"/> — обслуживает ли бот износ уже построенных фабрик (SPEC
    /// §5.6, см. <see cref="MaintainFactories"/>); по умолчанию да, чтобы обычные прогоны
    /// балансировки не спотыкались о новую механику незапланированно. <c>false</c> — «пренебрегающий»
    /// вариант, нужен харнессу балансировки, чтобы проверить, что фиксированной декларации без
    /// капремонта рано или поздно перестаёт хватать (запрос пользователя: механика не должна
    /// вырождаться в «поставил и забыл» — см. doc-comment <see cref="Game.Config.Economy.WearConfig"/>).
    /// </summary>
    public SimpleBot(Ulid teamId, Sector sector, ResolvedGameConfig config, bool maintainsFactories = true)
    {
        ArgumentNullException.ThrowIfNull(sector);
        ArgumentNullException.ThrowIfNull(config);

        TeamId = teamId;
        Sector = sector;
        _maintainsFactories = maintainsFactories;
        _sectorFactories = config.FactoryDefinitions
            .Where(f => f.Sector == sector)
            .OrderBy(f => f.Recipes[0].Output.Level)
            .ToList();

        if (_sectorFactories.Count == 0)
        {
            throw new ArgumentException($"Sector '{sector.Id}' has no factory definitions.", nameof(sector));
        }
    }

    /// <summary>Финальный продукт сектора этого бота — вершина его цепочки, ничем внутри неё дальше не потребляется.</summary>
    public Material FinalMaterial => _sectorFactories[^1].Recipes[0].Output;

    /// <summary>
    /// Берёт первый кредит (команды больше не получают стартовый капитал автоматически — это их
    /// первое собственное финансовое решение, SPEC §5.1; боту нужен детерминированный эквивалент
    /// для калибровки, поэтому сумма — <see cref="Game.Config.Session.StartingConditionsConfig.MaxStartingLoanAmount"/>),
    /// строит все УЖЕ разблокированные фабрики сектора (Блок 9.2 — более глубокие переделы
    /// открываются постепенно, не сразу; остальные достраивает по мере разблокировки
    /// <see cref="BuildNewlyUnlockedFactories"/>) и нанимает на каждую базовую численность рабочих,
    /// а также объявляет постоянное вложение в исследование следующего поколения на максимум
    /// потолка — чтобы бот вообще прогрессировал по пирамиде, а не застревал на стартовом поколении
    /// навсегда. Вызывать один раз, на первом ходу.
    /// </summary>
    public void BuildOutSectorChain(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.TakeLoan(TeamId, session.State.Config.Raw.StartingConditions.MaxStartingLoanAmount);
        session.SetGenerationResearchCommitment(TeamId, session.State.Config.Raw.GenerationResearch.MaxCommitmentPerTurn);

        BuildNewlyUnlockedFactories(session);
    }

    /// <summary>
    /// Достраивает те фабрики сектора, которые ещё не построены и уже разблокированы (Блок 9.2) —
    /// на первом ходу это подмножество, доступное сразу; на последующих — то, что только что
    /// открылось благодаря <see cref="GameSession.SetGenerationResearchCommitment"/>, объявленному в
    /// <see cref="BuildOutSectorChain"/>. Вызывать каждый ход решений, идемпотентно (уже
    /// построенные типы пропускаются).
    /// </summary>
    public void BuildNewlyUnlockedFactories(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var builtDefinitionIds = team.Factories.Select(f => f.Definition.Id).ToHashSet();
        var baseWorkerCount = session.State.Config.Raw.WorkerProductivity.BaseWorkerCount;
        foreach (var definition in _sectorFactories)
        {
            if (builtDefinitionIds.Contains(definition.Id) || definition.Recipes[0].Output.Level > team.UnlockedGeneration)
            {
                continue;
            }

            var built = (FactoryBuilt)session.BuildFactory(TeamId, definition.Id).Change;
            session.SetWorkerCount(TeamId, built.FactoryId, baseWorkerCount);
        }
    }

    /// <summary>Продаёт системе остаток финального продукта сверх объёма, уже обещанного действующими контрактами продажи.</summary>
    public void SellSurplusToSystem(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var stock = team.Warehouse.QuantityOf(FinalMaterial);
        var reserved = session.State.Contracts.Values
            .Where(c => c.SellerTeamId == TeamId && c.Status == ContractStatus.Active && c.Terms.Material == FinalMaterial)
            .Sum(c => c.Terms.Volume);

        var sellable = stock - reserved;
        if (sellable > 0)
        {
            session.SellToSystem(TeamId, FinalMaterial.Id, sellable);
        }
    }

    /// <summary>
    /// Число самых дешёвых ступеней <see cref="Game.Config.Economy.WearConfig.OverhaulTiers"/>
    /// (упорядоченных по убыванию состояния — от «почти не изношена» к «убита»), которые
    /// намеренно невыгодны и на которые бот не реагирует, — запрос пользователя: «сначала имеет
    /// смысл ничего не делать», кривая специально устроена так, чтобы чинить по любому чиху было
    /// расточительно. Бот дожидается ступени с индексом <see cref="IgnoredCheapestTierCount"/> и
    /// дальше, тем самым нащупывая баланс «чиню всё время» / «чиню слишком поздно» так же, как
    /// должна была бы играть команда.
    /// </summary>
    private const int IgnoredCheapestTierCount = 2;

    /// <summary>
    /// Поддерживает состояние уже построенных фабрик (SPEC §5.6): заказывает капремонт, как только
    /// состояние проваливается мимо первых <see cref="IgnoredCheapestTierCount"/> (намеренно
    /// невыгодных) ступеней — не при любом, даже самом мелком, отклонении от идеала. Ничего не
    /// делает, если бот сконструирован с <c>maintainsFactories: false</c> (см. doc-comment
    /// конструктора) — специально для харнесса балансировки, которому нужен «пренебрегающий» вариант.
    /// Идемпотентно, вызывать каждый ход решений.
    /// </summary>
    public void MaintainFactories(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maintainsFactories)
        {
            return;
        }

        var team = session.State.Teams[TeamId];
        var tiers = session.State.Config.Raw.Wear.OverhaulTiers;
        foreach (var factory in team.Factories)
        {
            if (factory.IsUnderRepair || factory.OverhaulRequested || factory.Condition >= 1m)
            {
                continue;
            }

            var tier = WearCalculator.SelectTier(factory.Condition, tiers);
            var tierIndex = tier is null ? -1 : tiers.ToList().IndexOf(tier);
            if (tierIndex < IgnoredCheapestTierCount)
            {
                continue;
            }

            session.SetOverhaulRequested(TeamId, factory.Id, requested: true);
        }
    }

    /// <summary>
    /// Раз в <see cref="ContractIntervalTurns"/> ходов заключает и сразу подтверждает простой
    /// spot-контракт на поставку финального продукта партнёру по сектору (SPEC §6). Обе заявки
    /// вычисляет сам вызывающий код — это не имитация переговоров двух независимых игроков, а
    /// механическое упражнение контрактной машины движка при автопрогоне (Блок 7.2). Ничего не
    /// подписывает, пока продавец ещё не построил фабрику финального продукта (Блок 9.2: она может
    /// быть временно недоступна — ждёт исследования следующего поколения) — иначе бот обещал бы
    /// поставку того, что физически не производит, и гарантированно сорвал бы её.
    /// </summary>
    public static void TrySignSimpleContract(GameSession session, SimpleBot seller, SimpleBot buyer, Random confirmationCodeRandom)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(seller);
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(confirmationCodeRandom);

        var turn = session.State.CurrentTurn;
        if (turn % ContractIntervalTurns != 0)
        {
            return;
        }

        var sellerTeam = session.State.Teams[seller.TeamId];
        if (!sellerTeam.Factories.Any(f => f.SelectedRecipe.Output == seller.FinalMaterial))
        {
            return;
        }

        var quote = session.State.Market.QuoteOf(seller.FinalMaterial.Id);
        var terms = new ContractTerms(
            ContractType.Spot, seller.FinalMaterial, ContractVolume, quote.Price,
            penaltyRate: session.State.Config.Raw.Contracts.DeliveryMissPenaltyRate,
            effectiveTurn: turn, spotDeliveryTurn: turn + 1, recurringEndTurn: null);

        var sellerProposal = new ContractProposal(buyer.TeamId, seller.TeamId, seller.TeamId, terms);
        var buyerProposal = new ContractProposal(buyer.TeamId, seller.TeamId, buyer.TeamId, terms);

        var result = session.SubmitContractProposals(sellerProposal, buyerProposal, confirmationCodeRandom);
        if (result.IsMatched)
        {
            session.ConfirmContract(result.Contract!.Id, TeamRole.Manager);
        }
    }
}
