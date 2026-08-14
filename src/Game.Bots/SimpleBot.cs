using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots;

/// <summary>
/// Ботовая стратегия команды, параметризованная по двум независимым осям (Блок 7.1-7.3.2,
/// BUILD_PLAN «Фаза 7», <c>docs/balancing-bots.md</c> §2) — полная вертикальная интеграция внутри
/// своего сектора — строит все типы фабрик сектора и нанимает базовую численность рабочих (SPEC
/// §5.6), продаёт остаток либо контрагенту через биржевой стакан (<see cref="ComputeSellOrders"/>/
/// <see cref="OrderBook"/>), либо системе (<see cref="SellSurplusToSystem"/>, SPEC §5.4), закупает
/// то, что не производит сам сектор, тем же стаканом (<see cref="ComputeBuyOrders"/>). Бот не ведёт
/// переговоры — сведение заявок стакана и согласование пары ботов на контракт целиком забота
/// вызывающего кода (<see cref="BotSessionRunner"/>, <see cref="OrderBook.Match"/>).
///
/// <para>
/// <c>leverage</c> (0..1, Блок 7.3.2) — аппетит к риску/кредиту: <c>0</c> — минимальный стартовый
/// заём, ничего не вкладывает сверх темпа, который тянет маржа, гасит долг добровольно при первой
/// возможности (<see cref="RepayDebt"/>); <c>1</c> — максимальный стартовый заём, вкладывает в R&amp;D
/// (командное и по каждой фабрике) на потолок, не спешит с добровольным погашением (платит только
/// обязательный минимум). Промежуточные значения — линейная интерполяция доли между полюсами (та же
/// схема, что и в `docs/balancing-bots.md` §2, «Промежуточные значения»).
/// </para>
/// <para>
/// <c>profile</c> (0..1, Блок 7.3.2) — распределение усилий по времени: <c>0</c> — фронт-лоадед,
/// вкладывает на полную с первого хода (значение по умолчанию — так вела себя <see cref="SimpleBot"/>
/// до Блока 7.3.2, регрессионный ориентир); <c>1</c> — бэк-лоадед, держит нулевые вложения почти всю
/// партию, резкий рывок ближе к концу. Момент переключения темпа — <see cref="UpdateInvestmentPace"/>.
/// </para>
///
/// <c>leverage≈1, profile≈0</c> (значения по умолчанию конструктора) — та самая единственная точка
/// сетки, которой был весь <see cref="SimpleBot"/> до Блока 7.3.2, не отдельный класс
/// (`docs/balancing-bots.md` §2: «SimpleBot в текущем виде станет одной из ячеек сетки»). Сама
/// жадность/агрессивность заявок стакана (<see cref="MinSellMarginRate"/>, <see
/// cref="MaxBuyPremiumRate"/> и т.д.) сознательно НЕ входит в сетку v1 — общая для всех ячеек
/// (`docs/balancing-bots.md` §2, «Осознанно не входит в сетку»).
/// </summary>
public sealed class SimpleBot
{
    /// <summary>
    /// Доля <see cref="Game.Config.Session.StartingConditionsConfig.MaxStartingLoanAmount"/>, которую
    /// берёт бот с минимальным <c>leverage</c> (Блок 7.3.2) — не ноль: без какого-то стартового
    /// капитала бот не может даже построить первую фабрику, «минимум кредита» — не «совсем без
    /// кредита». Деталь реализации, не зафиксирована в доке намеренно (`docs/balancing-bots.md` §4).
    /// </summary>
    private const decimal MinStartingLoanFraction = 0.25m;


    /// <summary>
    /// Во сколько «циклов» одной варки рецепта бот целится держать буфер закупаемого извне сырья
    /// (Блок 7.3.1) — заявка на покупку восполняет разницу между этим буфером и фактическим остатком.
    /// Намеренно грубая эвристика v1, не динамическая оптимизация (тот же уровень грубости, что и
    /// остальной идеальный зал, <c>docs/production-balance.md</c> §4).
    /// </summary>
    private const decimal BuyBufferCycles = 3m;

    /// <summary>Симметричный буфер для собственного потребления материала, который бот и производит, и продаёт на сторону (Блок 7.3.1) — не оголяет свою же цепочку ради продажи.</summary>
    private const decimal OwnUseBufferCycles = 2m;

    /// <summary>Надбавка сверх расчётной себестоимости, которую бот готов заплатить на закупке (Блок 7.3.1) — потолок цены заявки на покупку.</summary>
    private const decimal MaxBuyPremiumRate = 0.20m;

    /// <summary>Минимальная маржа сверх расчётной себестоимости, ниже которой бот не продаёт (Блок 7.3.1) — пол цены заявки на продажу.</summary>
    private const decimal MinSellMarginRate = 0.05m;

    /// <summary>Заявки мельче этого объёма не подаются вовсе — не засорять стакан пылью (Блок 7.3.1).</summary>
    private const decimal MinOrderVolume = 0.5m;

    /// <summary>Команда, за которую действует бот.</summary>
    public Ulid TeamId { get; }

    /// <summary>Сектор команды.</summary>
    public Sector Sector { get; }

    private readonly IReadOnlyList<FactoryDefinition> _sectorFactories;
    private readonly bool _maintainsFactories;
    private readonly decimal _leverage;
    private readonly decimal _profile;

    /// <summary>
    /// <paramref name="maintainsFactories"/> — обслуживает ли бот износ уже построенных фабрик (SPEC
    /// §5.6, см. <see cref="MaintainFactories"/>); по умолчанию да, чтобы обычные прогоны
    /// балансировки не спотыкались о новую механику незапланированно. <c>false</c> — «пренебрегающий»
    /// вариант, нужен харнессу балансировки, чтобы проверить, что фиксированной декларации без
    /// капремонта рано или поздно перестаёт хватать (запрос пользователя: механика не должна
    /// вырождаться в «поставил и забыл» — см. doc-comment <see cref="Game.Config.Economy.WearConfig"/>).
    /// <paramref name="leverage"/>/<paramref name="profile"/> (0..1, Блок 7.3.2) — две независимые оси
    /// сетки стратегий, см. doc-comment класса; значения по умолчанию воспроизводят поведение
    /// <see cref="SimpleBot"/> до Блока 7.3.2 (регрессионный ориентир).
    /// </summary>
    public SimpleBot(
        Ulid teamId, Sector sector, ResolvedGameConfig config,
        bool maintainsFactories = true, decimal leverage = 1m, decimal profile = 0m)
    {
        ArgumentNullException.ThrowIfNull(sector);
        ArgumentNullException.ThrowIfNull(config);
        if (leverage is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(leverage), leverage, "Leverage must be between 0 and 1.");
        }
        if (profile is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Profile must be between 0 and 1.");
        }

        TeamId = teamId;
        Sector = sector;
        _maintainsFactories = maintainsFactories;
        _leverage = leverage;
        _profile = profile;
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
    /// для калибровки) — сумма масштабируется <c>leverage</c> (Блок 7.3.2, см. doc-comment класса) от
    /// <see cref="MinStartingLoanFraction"/> потолка (<c>leverage=0</c>) до самого потолка
    /// (<see cref="Game.Config.Session.StartingConditionsConfig.MaxStartingLoanAmount"/>, <c>leverage=1</c>)
    /// — строит все УЖЕ разблокированные фабрики сектора (Блок 9.2 — более глубокие переделы
    /// открываются постепенно, не сразу; остальные достраивает по мере разблокировки
    /// <see cref="BuildNewlyUnlockedFactories"/>) и нанимает на каждую базовую численность рабочих.
    /// Темп вложений в R&amp;D (командный и по фабрикам) не объявляется здесь — им ведает
    /// <see cref="UpdateInvestmentPace"/>, вызываемая каждый ход решений отдельно, в том числе
    /// первый. Вызывать один раз, на первом ходу.
    /// </summary>
    public void BuildOutSectorChain(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var maxStartingLoan = session.State.Config.Raw.StartingConditions.MaxStartingLoanAmount;
        var startingLoanFraction = MinStartingLoanFraction + (1m - MinStartingLoanFraction) * _leverage;
        session.TakeLoan(TeamId, maxStartingLoan * startingLoanFraction);

        BuildNewlyUnlockedFactories(session);
    }

    /// <summary>
    /// Достраивает те фабрики сектора, которые ещё не построены и уже разблокированы (Блок 9.2) —
    /// на первом ходу это подмножество, доступное сразу; на последующих — то, что только что
    /// открылось благодаря командному исследованию поколений (<see cref="UpdateInvestmentPace"/>).
    /// Нанимает на каждую новую фабрику базовую численность рабочих; R&amp;D-вложение фабрике не
    /// назначает — тем же <see cref="UpdateInvestmentPace"/>, вызванным следом в тот же ход, чтобы
    /// новая фабрика не осталась на ход без объявленного темпа. Вызывать каждый ход решений,
    /// идемпотентно (уже построенные типы пропускаются).
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

    /// <summary>
    /// Держит темп вложений в R&amp;D (командное исследование поколений и каждая построенная фабрика
    /// разом, на одну и ту же долю потолка) в соответствии с осями стратегии (Блок 7.3.2, doc-comment
    /// класса): доля потолка — <c>0</c> до момента переключения, <c>leverage</c> после него. Момент
    /// переключения — <c>profile</c> доля длительности пресета сессии (<see
    /// cref="Game.Config.Session.SessionPresetConfig.MaxTurns"/> — публично известная команде верхняя
    /// граница, не тайный <see cref="GameSessionState.EndTurn"/>), от хода 0 (<c>profile=0</c> —
    /// вкладывает с первого хода) до последнего хода пресета (<c>profile=1</c> — почти вся партия
    /// на нулевых вложениях, резкий рывок под конец). «Скромный набор фабрик» бэк-лоадед профиля
    /// (`docs/balancing-bots.md` §2) — не отдельная логика, а естественное следствие нулевого темпа
    /// командного исследования поколений: новых уровней просто не открывается, пока не наступил
    /// момент переключения. Идемпотентно (пересчитывает и переобъявляет только при расхождении с уже
    /// объявленным значением), вызывать каждый ход решений, включая первый.
    /// </summary>
    public void UpdateInvestmentPace(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var maxTurns = session.State.Config.Raw.SessionPresets.Single(p => p.Id == session.State.PresetId).MaxTurns;
        var switchTurn = (int)Math.Round(_profile * maxTurns, MidpointRounding.AwayFromZero);
        var fraction = session.State.CurrentTurn >= switchTurn ? _leverage : 0m;

        var targetGenerationCommitment = session.State.Config.Raw.GenerationResearch.MaxCommitmentPerTurn * fraction;
        if (team.GenerationResearchCommitmentPerTurn != targetGenerationCommitment)
        {
            session.SetGenerationResearchCommitment(TeamId, targetGenerationCommitment);
        }

        var targetRndCommitment = session.State.Config.Raw.Rnd.MaxCommitmentPerTurn * fraction;
        foreach (var factory in team.Factories)
        {
            if (factory.RndCommitmentPerTurn != targetRndCommitment)
            {
                session.SetRndCommitment(TeamId, factory.Id, targetRndCommitment);
            }
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
    /// Добровольно гасит долг сверх обязательного платежа (Блок 7.3.1-7.3.2, <c>docs/balancing-bots.md</c>
    /// §1-2) — без этого взятый кредит никогда не уменьшается, кроме фиксированной доли за ход
    /// (<see cref="Game.Engine.FinanceCalculator.CalculateMandatoryRepayment"/>). Свободный остаток
    /// сверх буфера на ближайший ход (зарплата всех рабочих команды, содержание фабрик, обязательный
    /// платёж, проценты, уже объявленные R&amp;D-вложения — свои и командные) гасится не целиком, а в
    /// доле <c>(1 - leverage)</c> (Блок 7.3.2, doc-comment класса): <c>leverage=1</c> — доля 0, ничего
    /// не гасит сверх обязательного, весь свободный кэш остаётся на реинвестирование; <c>leverage=0</c>
    /// — доля 1, гасит весь свободный остаток при первой возможности. Ничего не делает, если долга нет
    /// или буфер уже съедает весь баланс. Идемпотентно, вызывать каждый ход решений.
    /// </summary>
    public void RepayDebt(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        if (team.Debt <= 0m)
        {
            return;
        }

        var config = session.State.Config.Raw;
        var totalWorkers = team.Factories.Sum(f => f.Workers);
        var reputationPercentage = session.GetReputation(TeamId).Percentage;

        var buffer = FinanceCalculator.CalculateSalaries(totalWorkers, config.WorkerProductivity)
                     + FinanceCalculator.CalculateFactoryUpkeep(team.Factories, config.FactoryDefinitions, config.Wear)
                     + FinanceCalculator.CalculateMandatoryRepayment(team, config.StartingConditions)
                     + FinanceCalculator.CalculateInterest(team, config.StartingConditions, reputationPercentage)
                     + team.Factories.Sum(f => f.RndCommitmentPerTurn)
                     + team.GenerationResearchCommitmentPerTurn;

        var repayable = (team.Balance - buffer) * (1m - _leverage);
        if (repayable > 0m)
        {
            session.RepayLoan(TeamId, Math.Min(repayable, team.Debt));
        }
    }

    /// <summary>
    /// Заявки на продажу для биржевого стакана (Блок 7.3.1, <see cref="OrderBook"/>) — по каждому
    /// материалу, который команда сама производит: остаток на складе за вычетом того, что уже
    /// обещано действующими контрактами продажи, и буфера на собственное потребление, если материал
    /// — ещё и вход одного из своих же рецептов (см. <see cref="OwnUseBufferCycles"/>, не оголяет
    /// свою цепочку ради продажи на сторону). Цена — себестоимость плюс минимальная маржа (<see
    /// cref="MinSellMarginRate"/>).
    /// </summary>
    public IReadOnlyList<TradeOrder> ComputeSellOrders(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var recipeBook = session.State.Config.RecipeBook;
        var rawMaterialCosts = RawMaterialCosts(session);

        var orders = new List<TradeOrder>();
        foreach (var material in team.Factories.Select(f => f.SelectedRecipe.Output).Distinct())
        {
            var stock = team.Warehouse.QuantityOf(material);
            var reservedByContracts = session.State.Contracts.Values
                .Where(c => c.SellerTeamId == TeamId && c.Status == ContractStatus.Active && c.Terms.Material == material)
                .Sum(c => c.Terms.Volume);
            var ownUseBuffer = team.Factories
                .SelectMany(f => f.SelectedRecipe.Inputs)
                .Where(input => input.Material == material)
                .Sum(input => input.Quantity * OwnUseBufferCycles);

            var sellable = stock - reservedByContracts - ownUseBuffer;
            if (sellable < MinOrderVolume || !TryCalculateUnitCost(material, recipeBook, rawMaterialCosts, out var unitCost))
            {
                continue;
            }

            orders.Add(new TradeOrder
            {
                TeamId = TeamId,
                Material = material,
                Volume = sellable,
                LimitPrice = unitCost * (1m + MinSellMarginRate),
            });
        }

        return orders;
    }

    /// <summary>
    /// Заявки на покупку для биржевого стакана (Блок 7.3.1, <see cref="OrderBook"/>) — по каждому
    /// материалу, который нужен одному из построенных рецептов команды, но не производится ею самой
    /// (то, что физически может дать только другой сектор — свои сырьё и переделы уже закрыты
    /// строительством всей цепочки сектора, см. <see cref="BuildNewlyUnlockedFactories"/>). Целится в
    /// буфер на <see cref="BuyBufferCycles"/> варок рецепта; заявка — только на нехватку до этого
    /// буфера. Цена — себестоимость плюс потолок надбавки (<see cref="MaxBuyPremiumRate"/>).
    /// </summary>
    public IReadOnlyList<TradeOrder> ComputeBuyOrders(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var team = session.State.Teams[TeamId];
        var recipeBook = session.State.Config.RecipeBook;
        var rawMaterialCosts = RawMaterialCosts(session);
        var ownProducedMaterials = team.Factories.Select(f => f.SelectedRecipe.Output).ToHashSet();

        var neededPerCycle = team.Factories
            .SelectMany(f => f.SelectedRecipe.Inputs)
            .Where(input => !ownProducedMaterials.Contains(input.Material))
            .GroupBy(input => input.Material)
            .ToDictionary(g => g.Key, g => g.Sum(input => input.Quantity));

        var orders = new List<TradeOrder>();
        foreach (var (material, perCycle) in neededPerCycle)
        {
            var targetBuffer = perCycle * BuyBufferCycles;
            var deficit = targetBuffer - team.Warehouse.QuantityOf(material);
            if (deficit < MinOrderVolume || !TryCalculateUnitCost(material, recipeBook, rawMaterialCosts, out var unitCost))
            {
                continue;
            }

            orders.Add(new TradeOrder
            {
                TeamId = TeamId,
                Material = material,
                Volume = deficit,
                LimitPrice = unitCost * (1m + MaxBuyPremiumRate),
            });
        }

        return orders;
    }

    /// <summary>Котировки текущего рынка на всё сырьё, у которого уже есть котировка, — вход для <see cref="CostCalculator.CalculateUnitCost"/> (тот же приём, что <c>DashboardDisplay.TryCalculateUnitCost</c> в Game.Web).</summary>
    private static IReadOnlyDictionary<Material, decimal> RawMaterialCosts(GameSession session) =>
        session.State.Config.Materials.Values
            .Where(m => m.IsRawMaterial && session.State.Market.HasQuote(m.Id))
            .ToDictionary(m => m, m => session.State.Market.QuoteOf(m.Id).Price);

    /// <summary>
    /// Обёртка над <see cref="CostCalculator.CalculateUnitCost"/>, не падающая, если по какому-то
    /// сырью в цепочке ещё нет котировки (например, самый первый ход) — заявка в этом случае просто
    /// не подаётся в этот раз, а не роняет весь прогон.
    /// </summary>
    private static bool TryCalculateUnitCost(
        Material material, RecipeBook recipeBook, IReadOnlyDictionary<Material, decimal> rawMaterialCosts, out decimal unitCost)
    {
        try
        {
            unitCost = CostCalculator.CalculateUnitCost(material, recipeBook, rawMaterialCosts);
            return true;
        }
        catch (ArgumentException)
        {
            unitCost = 0m;
            return false;
        }
    }
}
