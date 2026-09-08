using Game.Config.Economy;
using Game.Config.Loading;
using Game.Domain;

namespace Game.Engine.Tests;

/// <summary>
/// «Требует внимания» (docs/TODO.md №7). Проверяется не только то, что сигнал появляется, но и — на
/// равных правах — то, что он НЕ появляется: панель адресована человеку, который играет первый раз и
/// ещё не умеет отличать ложную тревогу от настоящей, поэтому ложное срабатывание здесь дороже
/// пропуска (см. doc-comment <see cref="TeamAttentionCalculator"/>).
/// </summary>
public class TeamAttentionCalculatorTests
{
    /// <summary>Один полный ход из фазы решений: решения применяются расчётом СЛЕДУЮЩЕГО хода (порядок фаз — расчёт, потом решения), поэтому «сыграть ход» — это перейти в расчёт, посчитать его и вернуться в решения.</summary>
    private static void PlayTurn(GameSession session)
    {
        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        session.RunTick(new Random(1));
        session.AdvancePhase(PhaseTransitionTrigger.Timer);
    }

    private static (GameSession Session, Ulid TeamId) StartInDecisionPhase(ResolvedGameConfig? config = null)
    {
        var (session, teamId) = TestGameConfig.StartGameSessionWithOneTeam(config: config);
        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        return (session, teamId);
    }

    private static Ulid Build(GameSession session, Ulid teamId, FactoryDefinition definition, int workers)
    {
        var factoryId = ((FactoryBuilt)session.BuildFactory(teamId, definition.Id, definition.Recipes[0].Id).Change).FactoryId;
        if (workers > 0)
        {
            session.SetWorkerCount(teamId, factoryId, workers);
        }

        return factoryId;
    }

    private static IReadOnlyList<TeamAttentionCalculator.AttentionItem> Calculate(GameSession session, Ulid teamId) =>
        TeamAttentionCalculator.Calculate(session.Entries, session.State, teamId);

    [Fact]
    public void Healthy_Team_Gets_An_Empty_List()
    {
        var (session, teamId) = StartInDecisionPhase();
        Build(session, teamId, TestGameConfig.Mine, workers: 5);
        PlayTurn(session);
        PlayTurn(session);
        PlayTurn(session);

        Assert.Empty(Calculate(session, teamId));
    }

    /// <summary>
    /// Тот самый случай из разбора лога 2026-08-09, ради которого пункт и заведён: фабрика стоит без
    /// сырья ход за ходом, а экран на 25-м ходу выглядит ровно так же, как на первом.
    /// </summary>
    [Fact]
    public void Factory_Starved_Several_Turns_In_A_Row_Is_Reported_With_The_Missing_Material()
    {
        var (session, teamId) = StartInDecisionPhase();
        var millId = Build(session, teamId, TestGameConfig.Mill, workers: 5);
        PlayTurn(session);
        PlayTurn(session);

        var item = Assert.Single(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput>());
        Assert.Equal(millId, item.FactoryId);
        Assert.Equal(TestGameConfig.Ore.Id, item.MaterialId);
        Assert.Equal(2, item.TurnsInARow);
        Assert.True(item.ShortfallPerTurn > 0m);
    }

    /// <summary>
    /// Один ход недобора — не повод: он уже виден на самой карточке фабрики красной рамкой
    /// (<c>FactoryOverviewList</c>), а смысл сигнала именно в длительности.
    /// </summary>
    [Fact]
    public void A_Single_Starved_Turn_Is_Not_Reported()
    {
        var (session, teamId) = StartInDecisionPhase();
        Build(session, teamId, TestGameConfig.Mill, workers: 5);
        PlayTurn(session);

        Assert.Empty(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput>());
    }

    /// <summary>Сырьё появилось — сигнал обязан уйти сам, даже если в журнале осталась длинная история простоя.</summary>
    [Fact]
    public void Starvation_Stops_Being_Reported_Once_The_Input_Is_On_The_Shelf()
    {
        var (session, teamId) = StartInDecisionPhase();
        Build(session, teamId, TestGameConfig.Mill, workers: 5);
        PlayTurn(session);
        PlayTurn(session);
        Assert.NotEmpty(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput>());

        // Два рудника, не один: рецепт листа берёт 2 руды на единицу, и завод при пяти рабочих
        // просит 10 руды за ход — ровно вдвое больше, чем даёт один рудник.
        Build(session, teamId, TestGameConfig.Mine, workers: 5);
        Build(session, teamId, TestGameConfig.Mine, workers: 5);
        for (var i = 0; i < 5; i++)
        {
            PlayTurn(session);
        }

        Assert.Empty(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.FactoryStarvedOfInput>());
    }

    [Fact]
    public void Factory_Without_Workers_Is_Reported()
    {
        var (session, teamId) = StartInDecisionPhase();
        var mineId = Build(session, teamId, TestGameConfig.Mine, workers: 0);

        var item = Assert.Single(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers>());
        Assert.Equal(mineId, item.FactoryId);
    }

    /// <summary>Наём объявлен, рабочие выйдут расчётом этого же хода — команда уже среагировала, повода нет.</summary>
    [Fact]
    public void Factory_With_Declared_Hiring_Is_Not_Reported_As_Workerless()
    {
        var (session, teamId) = StartInDecisionPhase();
        Build(session, teamId, TestGameConfig.Mine, workers: 5);

        Assert.Empty(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.FactoryWithoutWorkers>());
    }

    [Fact]
    public void Warehouse_Over_The_Free_Limit_Is_Reported_With_The_Fee()
    {
        var config = TestGameConfig.BuildWithWarehouse(new WarehouseConfig { FreeCapacity = 1m, OverageFeePerUnit = 0.5m });
        var (session, teamId) = StartInDecisionPhase(config);
        Build(session, teamId, TestGameConfig.Mine, workers: 5);
        PlayTurn(session);

        var item = Assert.Single(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.WarehouseOverFreeCapacity>());
        Assert.True(item.OverageQuantity > 0m);
        Assert.Equal(item.OverageQuantity * 0.5m, item.FeePerTurn);
    }

    /// <summary>
    /// Поставка в ближайшем расчёте, которую нечем закрыть: штраф спишется уже следующим тиком, и это
    /// последний ход, когда игрок ещё может что-то сделать.
    /// </summary>
    [Fact]
    public void Delivery_Due_In_The_Next_Settlement_Without_Stock_Is_Reported_With_The_Penalty()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(
            buyerId, sellerId, volume: 10m, unitPrice: 20m, penaltyRate: 0.1m, effectiveTurn: 2, deliveryTurn: 2);
        var contractId = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1)).Contract!.Id;
        session.ConfirmContract(contractId, TeamRole.Manager, sellerId);

        var item = Assert.Single(
            TeamAttentionCalculator.Calculate(session.Entries, session.State, sellerId)
                .OfType<TeamAttentionCalculator.AttentionItem.DeliveryDueAndShort>());
        Assert.Equal(contractId, item.ContractId);
        Assert.Equal(TestGameConfig.Sheet.Id, item.MaterialId);
        Assert.Equal(10m, item.Shortfall);
        Assert.Equal(10m * 20m * 0.1m, item.Penalty);
    }

    /// <summary>Покупателю о срыве поставки контрагента не сообщаем: это не его решение и не его действие.</summary>
    [Fact]
    public void The_Buyer_Is_Not_Warned_About_The_Sellers_Delivery()
    {
        var (session, buyerId, sellerId) = TestGameConfig.StartGameSessionWithTwoTeams();
        session.AdvancePhase(PhaseTransitionTrigger.Timer);
        var (buyerProposal, sellerProposal) = TestGameConfig.MatchingSheetSpotProposals(buyerId, sellerId, effectiveTurn: 2, deliveryTurn: 2);
        var contractId = session.SubmitContractProposals(buyerProposal, sellerProposal, new Random(1)).Contract!.Id;
        session.ConfirmContract(contractId, TeamRole.Manager, sellerId);

        Assert.Empty(
            TeamAttentionCalculator.Calculate(session.Entries, session.State, buyerId)
                .OfType<TeamAttentionCalculator.AttentionItem.DeliveryDueAndShort>());
    }

    /// <summary>
    /// Капремонт дорожает ступенями, и переход между ними считается точно — декей детерминирован.
    /// Льготный период здесь укорочен специально: у общего тестового конфига он намеренно огромный,
    /// чтобы износ не мешал тестам, которые не про него.
    /// </summary>
    [Fact]
    public void Overhaul_About_To_Move_To_A_More_Expensive_Tier_Is_Reported()
    {
        var raw = TestGameConfig.Resolved.Raw;
        var config = GameConfigLoader.Load(raw with
        {
            Wear = raw.Wear with { GracePeriodTurns = 1, BaseWearRatePerTurn = 0.05m, AccelerationFactorPerTurn = 0.01m },
        });
        var (session, teamId) = StartInDecisionPhase(config);
        var mineId = Build(session, teamId, TestGameConfig.Mine, workers: 5);
        PlayTurn(session);
        PlayTurn(session);

        var item = Assert.Single(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive>());
        Assert.Equal(mineId, item.FactoryId);
        Assert.Equal("prevention", item.CurrentTierId);
        Assert.Equal("scheduled", item.NextTierId);
        Assert.InRange(item.TurnsUntil, 1, TeamAttentionCalculator.SoonHorizonTurns);
    }

    /// <summary>Команда уже заказала капремонт — предупреждать не о чем, решение принято.</summary>
    [Fact]
    public void A_Requested_Overhaul_Silences_The_Wear_Warning()
    {
        var raw = TestGameConfig.Resolved.Raw;
        var config = GameConfigLoader.Load(raw with
        {
            Wear = raw.Wear with { GracePeriodTurns = 1, BaseWearRatePerTurn = 0.05m, AccelerationFactorPerTurn = 0.01m },
        });
        var (session, teamId) = StartInDecisionPhase(config);
        var mineId = Build(session, teamId, TestGameConfig.Mine, workers: 5);
        PlayTurn(session);
        PlayTurn(session);

        session.SetOverhaulRequested(teamId, mineId, requested: true);

        Assert.Empty(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.OverhaulGetsMoreExpensive>());
    }

    /// <summary>
    /// Расход обгоняет собственный выпуск: рудник даёт 5 руды за ход, завод просит 10 — остатка
    /// хватит на считаные ходы, и об этом стоит знать до того, как завод встанет.
    /// </summary>
    [Fact]
    public void Material_Consumed_Faster_Than_Produced_Is_Reported_Before_It_Runs_Out()
    {
        var (session, teamId) = StartInDecisionPhase();
        Build(session, teamId, TestGameConfig.Mine, workers: 5);
        PlayTurn(session);
        PlayTurn(session);

        // Рудник намыл 10 руды за два хода, завод просит 10 за ход при собственной добыче 5 —
        // дефицит 5 в ход, значит остатка хватит ровно на два хода, это внутри горизонта.
        var millId = Build(session, teamId, TestGameConfig.Mill, workers: 5);

        var item = Assert.Single(Calculate(session, teamId).OfType<TeamAttentionCalculator.AttentionItem.MaterialRunningOut>());
        Assert.Equal(TestGameConfig.Ore.Id, item.MaterialId);
        Assert.Contains(millId, item.AffectedFactoryIds);
        Assert.InRange(item.TurnsUntil, 1, TeamAttentionCalculator.SoonHorizonTurns);
    }

    /// <summary>
    /// Сроки обязаны считаться от ближайшего расчёта, а не от номера текущего хода: в фазу решений
    /// хода N расчёт этого хода уже прошёл. Ошибка здесь означала бы предупреждение ровно на ход
    /// позже, чем нужно, — когда штраф уже списан.
    /// </summary>
    [Fact]
    public void Upcoming_Settlement_Turn_Is_The_Next_Turn_During_The_Decision_Phase()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();
        Assert.Equal(TurnPhase.Settlement, session.State.CurrentPhase);
        Assert.Equal(session.State.CurrentTurn, TeamAttentionCalculator.UpcomingSettlementTurn(session.State));

        session.AdvancePhase(PhaseTransitionTrigger.Timer);

        Assert.Equal(TurnPhase.Decision, session.State.CurrentPhase);
        Assert.Equal(session.State.CurrentTurn + 1, TeamAttentionCalculator.UpcomingSettlementTurn(session.State));
    }

    [Fact]
    public void Unknown_Team_Gets_An_Empty_List()
    {
        var (session, _) = TestGameConfig.StartGameSessionWithOneTeam();

        Assert.Empty(TeamAttentionCalculator.Calculate(session.Entries, session.State, Ulid.NewUlid()));
    }
}
