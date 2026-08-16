namespace Game.Bots.Llm.Tests;

/// <summary>Проверяет текстовый срез состояния (шаг 4 плана LLM-ботов) на реальной сессии, без единого обращения к LLM.</summary>
public sealed class BotStateSnapshotBuilderTests
{
    [Fact]
    public void Build_IncludesAllSectionsOnEmptyTeam()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains("=== Turn 1 of 20, phase Decision ===", snapshot);
        Assert.Contains("YOUR TEAM (sector A)", snapshot);
        Assert.Contains("YOUR FACTORIES", snapshot);
        Assert.Contains("(none yet)", snapshot);
        Assert.Contains("WAREHOUSE", snapshot);
        Assert.Contains("(empty)", snapshot);
        Assert.Contains("MARKET (your sector", snapshot);
        Assert.Contains("CONTRACTS INVOLVING YOU", snapshot);
        Assert.Contains("(none)", snapshot);
        Assert.Contains("TEAM RANKING", snapshot);
        Assert.Contains("Команда", snapshot);
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
    public void Build_AfterBuildingFactory_ListsItWithRealId()
    {
        var (session, teamId) = TestSession.StartSingleTeamSession();
        session.BuildFactory(teamId, "iron-mine");
        var factoryId = session.State.Teams[teamId].Factories[0].Id;

        var snapshot = BotStateSnapshotBuilder.Build(session, teamId);

        Assert.Contains($"factoryId={factoryId}", snapshot);
        Assert.Contains("type=iron-mine", snapshot);
        Assert.Contains("status=operating", snapshot);
    }

    [Fact]
    public void Build_UnknownTeam_Throws()
    {
        var (session, _) = TestSession.StartSingleTeamSession();

        Assert.Throws<ArgumentException>(() => BotStateSnapshotBuilder.Build(session, Ulid.NewUlid()));
    }
}
