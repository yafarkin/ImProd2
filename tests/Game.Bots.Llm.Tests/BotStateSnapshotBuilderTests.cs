using Game.Engine;

namespace Game.Bots.Llm.Tests;

/// <summary>Проверяет текстовый срез состояния (шаг 4 плана LLM-ботов) на реальной сессии, без единого обращения к LLM.</summary>
public sealed class BotStateSnapshotBuilderTests
{
    [Fact]
    public void Build_IncludesAllSectionsOnEmptyTeam()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains("=== Turn 1, phase Decision ===", snapshot);
        Assert.DoesNotContain("of 20", snapshot); // EndTurn must not leak — real players never see it (Team.razor)
        Assert.Contains("YOUR TEAM (sector A)", snapshot);
        Assert.Contains("YOUR FACTORIES", snapshot);
        Assert.Contains("(none yet)", snapshot);
        Assert.Contains("WAREHOUSE", snapshot);
        Assert.Contains("(empty)", snapshot);
        Assert.Contains("MARKET (your sector", snapshot);
        Assert.Contains("CONTRACTS INVOLVING YOU", snapshot);
        Assert.Contains("(none)", snapshot);
        Assert.Contains("PUBLIC TRADE OFFERS", snapshot);
        Assert.Contains("(none open right now)", snapshot);
        Assert.Contains("TEAM RANKING", snapshot);
        Assert.Contains("Команда", snapshot);
    }

    [Fact]
    public void Build_AfterPostingATradeOffer_ShowsItWithASelfMarker()
    {
        // Запрос пользователя 2026-08-17: доска публичных заявок должна быть видна всем ботам, а не
        // только автору (в отличие от старой доски потребностей, которую до этого никто не читал).
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var posted = session.PostTradeOffer(teamId, Game.Domain.TradeOfferDirection.Sell, "ore", Game.Domain.ContractType.Spot, 20m, 5m, 8m);
        var offerId = ((TradeOfferPosted)posted.Change).TradeOfferId;

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains($"tradeOfferId={offerId} Команда (you) selling materialId=ore", snapshot);
        Assert.Contains("volume=20 (one-off) price=5.00-8.00", snapshot);
        Assert.Contains("turns_left=3", snapshot);
    }

    [Fact]
    public void Build_ListsBuildableFactoryTypesWithExactCatalogIds()
    {
        // Живой прогон 2026-08-16: без этой секции модель однажды придумала "IronMine" вместо
        // настоящего "iron-mine" — доменная ошибка на первой попытке из-за нехватки данных, не из-за
        // самой модели. Регресс на то, что точный id теперь есть в снапшоте.
        var (session, teamId) = TestSession.StartSingleTeamSession();

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains("FACTORY TYPES IN YOUR SECTOR (A)", snapshot);
        Assert.Contains("factoryDefinitionId=iron-mine", snapshot);
        Assert.Contains("status=unlocked", snapshot);
        Assert.DoesNotContain("oil-well", snapshot); // sector B, must not leak into sector A's list
    }

    [Fact]
    public void Build_FactoryTypesSection_StaysWithinGenerationPlusOneAndDoesNotDropInRangeTypes()
    {
        // Живой прогон на реальном конфиге стадии 1 (26 типов фабрик в одном секторе, 2026-08-16,
        // см. TODO.md #20): без ограничения по поколению список всех типов переполнил контекст-окно
        // модели через несколько ходов и обвалил прогон HTTP 400. Пилотный конфиг тут маленький
        // (3 типа сектора А, поколения 0/1/2, все укладываются в unlockedGeneration(1)+1=2) — этот
        // тест лишь подтверждает, что на маленьком каталоге ограничение ничего не откусывает.
        var (session, teamId) = TestSession.StartSingleTeamSession();

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains("factoryDefinitionId=iron-mine", snapshot);
        Assert.Contains("factoryDefinitionId=steel-mill", snapshot);
        Assert.Contains("factoryDefinitionId=rolling-mill", snapshot);
        Assert.DoesNotContain("more factory type", snapshot);
    }

    [Fact]
    public void Build_AfterBuildingFactory_ListsItWithRealId()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains($"factoryId={factoryId}", snapshot);
        Assert.Contains("type=iron-mine", snapshot);
        Assert.Contains("overhaulRequested=false", snapshot);
        Assert.Contains("status=operating", snapshot);
    }

    [Fact]
    public void Build_AfterRequestingOverhaul_ShowsOverhaulRequestedTrue()
    {
        // Запрос пользователя 2026-08-19: боту должно быть видно, уже запросил ли он капремонт сам,
        // чтобы не путать это с «фабрика уже сломалась» (status=operating остаётся тем же).
        var (session, teamId) = TestSession.StartSingleTeamSession();
        var random = new Random(1);
        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;

        // Свежепостроенная фабрика в идеальном состоянии — запросить капремонт для неё нельзя (движок
        // отказывает, незачем). Износ начинается не раньше GracePeriodTurns (пилотный конфиг — 8), 10
        // ходов settlement гарантированно уводят condition ниже 100%.
        for (var i = 0; i < 10; i++)
        {
            TestSession.SettleOneTurn(session, random);
        }

        session.SetOverhaulRequested(teamId, factoryId, true);

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains("overhaulRequested=true", snapshot);
    }

    [Fact]
    public void Build_UnknownTeam_Throws()
    {
        var (session, _) = TestSession.StartSingleTeamSession();

        Assert.Throws<ArgumentException>(() => BotStateSnapshotBuilder.Build(session, Ulid.NewUlid()));
    }

    [Fact]
    public void Build_OnlyOneSectorOccupied_OmitsCrossSectorDemandSection()
    {
        // gameconfig.pilot.json объявляет сектор Б в справочнике, но StartSingleTeamSession сажает
        // ВСЕ команды в А — секция должна судить по реально занятым секторам, не по каталогу конфига
        // (см. doc-comment BotStateSnapshotBuilder.AppendCrossSectorDemand).
        var (session, teamId) = TestSession.StartSingleTeamSession();

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.DoesNotContain("CROSS-SECTOR DEMAND", snapshot);
    }

    [Fact]
    public void Build_TwoSectorsOccupiedButNoCrossDependency_ShowsSectionWithBothFallbackLines()
    {
        // Сектора А/Б пилотного конфига (ore→sheet→rebar, oil→plastic) не зависят друг от друга ни в
        // одном рецепте — секция должна появиться (кто-то в другом секторе есть), но честно сказать,
        // что торговать нечем, а не молчать и не выдумывать несуществующую зависимость.
        var (session, teamAId, teamBId) = TestSession.StartTwoSectorSession();

        var snapshot = BotStateSnapshotBuilder.Build(session, teamAId);

        Assert.Contains("CROSS-SECTOR DEMAND", snapshot);
        Assert.Contains("No other sector's recipe currently consumes a material your sector produces.", snapshot);
        Assert.Contains("None of your recipes need a material from another sector.", snapshot);
    }

    [Fact]
    public void Build_TwoSectorsWithARealCrossDependency_ListsTheExactMaterialsOnBothSides()
    {
        // Прямой запрос пользователя 2026-08-20, по следам первого прогона стадии 2
        // (_2bot_gpt_oss_20b_2stage_v1, см. _bots_llm/NOTES.md): оба бота выставили на доску заявок
        // материал, который другому сектору физически не нужен — эта секция должна называть РОВНО те
        // материалы, которые другой сектор реально потребляет, не весь ассортимент своего сектора.
        var config = CrossSectorConfigBuilder.Build();
        var teamAId = Ulid.NewUlid();
        var teamBId = Ulid.NewUlid();
        var teams = new List<TeamSpec>
        {
            new() { Id = teamAId, Name = "Команда А", SectorId = "A" },
            new() { Id = teamBId, Name = "Команда Б", SectorId = "B" },
        };
        var session = GameSession.StartWithEndTurn(config, "short", 15, teams);
        session.AdvancePhase(PhaseTransitionTrigger.Facilitator);

        var snapshotA = BotStateSnapshotBuilder.Build(session, teamAId);
        var snapshotB = BotStateSnapshotBuilder.Build(session, teamBId);

        // А производит a-part, которое ест рецепт Б — должно попасть в "продать" у А.
        Assert.Contains("Materials YOUR sector produces that another sector's recipes actually consume", snapshotA);
        Assert.Contains("a-part", snapshotA);
        Assert.Contains("None of your recipes need a material from another sector.", snapshotA);

        // Б, наоборот, само ничего не продаёт А (нет обратной зависимости), но ЗАВИСИТ от a-part.
        Assert.Contains("No other sector's recipe currently consumes a material your sector produces.", snapshotB);
        Assert.Contains("Materials YOUR OWN recipes need that come from another sector", snapshotB);
        Assert.Contains("a-part", snapshotB);
    }
}
