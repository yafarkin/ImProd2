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

    public SimpleBot(Ulid teamId, Sector sector, ResolvedGameConfig config)
    {
        ArgumentNullException.ThrowIfNull(sector);
        ArgumentNullException.ThrowIfNull(config);

        TeamId = teamId;
        Sector = sector;
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
