using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Каркас экрана команды (docs/manager-ui/README.md §8, блок 1): шапка на каждом разделе, четыре
/// раздела на своих адресах, состав команды — отдельно, из меню. Раньше всё жило на одном
/// <c>/team</c> семью вкладками.
/// </summary>
public class TeamShellTests
{
    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    [Theory]
    [InlineData("/team", "Требует внимания")]
    [InlineData("/team/production", "Цепочка производства")]
    [InlineData("/team/deals", "Новая заявка")]
    [InlineData("/team/needs", "Доска потребностей зала")]
    [InlineData("/team/analytics", "История операций")]
    [InlineData("/team/members", "Состав команды")]
    [InlineData("/team/no-such-section", "Требует внимания")]
    public async Task Each_Section_Has_Its_Own_Address_And_The_Header(string path, string expectedHeading)
    {
        var html = await RenderAsManager(path);

        Assert.Contains(expectedHeading, html);

        // Шапка — на любом разделе: команда, ход с фазой и баланс.
        Assert.Contains("team-topbar", html);
        Assert.Contains("Тета", html);
        Assert.Contains("Ход 1 · Решения", html);
        Assert.Contains("Баланс", html);
    }

    [Fact]
    public async Task Sections_Do_Not_Render_Each_Other()
    {
        // Раньше все семь вкладок рендерились всегда и только прятались через display:none.
        var html = await RenderAsManager("/team/analytics");

        Assert.DoesNotContain("Цепочка производства", html);
        Assert.DoesNotContain("Новая заявка", html);
    }

    [Fact]
    public async Task The_Team_Roster_Lives_Behind_The_Menu_Not_On_The_Main_Section()
    {
        var html = await RenderAsManager("/team");

        Assert.DoesNotContain("Добавить переговорщика", html);
        Assert.Contains("href=\"/team/members\"", html);
    }

    [Fact]
    public async Task The_Factory_Page_Opens_The_Factory_From_The_Address()
    {
        // Так «Требует внимания» ведёт к фабрике: разделы не делят экземпляр страницы, и какую фабрику
        // открыть, передаётся через адрес — /team/factory/{id}.
        var html = await RenderAsManager((session, teamId) =>
        {
            var mineDefinitionId = MineDefinitionId(session, teamId);
            session.BuildFactory(teamId, mineDefinitionId);
            session.BuildFactory(teamId, mineDefinitionId);
            var factories = session.State.Teams[teamId].Factories.OrderBy(f => f.Id).ToList();
            return $"/team/factory/{factories[1].Id}?tab=wear";
        });

        Assert.Contains("Рудник №2", html);
        // Все рычаги — на одной странице, без вкладок.
        Assert.Contains("id=\"factory-workers\"", html);
        Assert.Contains("id=\"factory-wear\"", html);
        Assert.Contains("id=\"factory-rnd\"", html);
        Assert.Contains("id=\"factory-recipe\"", html);
        Assert.Contains("← Цепочка производства", html);
        // Страница фабрики — часть «Производства»: подсвечен он, а не «Ход».
        Assert.Contains("<a href=\"team/production\" class=\"active\"", html);
        Assert.DoesNotContain("<a href=\"team\" class=\"active\"", html);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAV")] // правильный формат, но фабрики с таким Id у команды нет
    public async Task An_Unknown_Factory_Address_Does_Not_Open_Another_Factory(string factoryId)
    {
        // Раньше неизвестный Id молча подменялся первой фабрикой команды — под чужим адресом
        // управляющий оказывался у рычагов не той фабрики.
        var html = await RenderAsManager((session, teamId) =>
        {
            session.BuildFactory(teamId, MineDefinitionId(session, teamId));
            return $"/team/factory/{factoryId}";
        });

        Assert.Contains("Такой фабрики у вашей команды нет", html);
        Assert.DoesNotContain("id=\"factory-workers\"", html);
    }

    [Fact]
    public async Task Production_Groups_Same_Type_Factories_And_Puts_Stock_After_Its_Level()
    {
        var html = await RenderAsManager((session, teamId) =>
        {
            var mineDefinitionId = MineDefinitionId(session, teamId);
            session.BuildFactory(teamId, mineDefinitionId);
            session.BuildFactory(teamId, mineDefinitionId);
            return "/team/production";
        });

        Assert.Contains("Передел 0", html);
        Assert.Contains("Рудник ×2", html);
        Assert.Contains("Склад после передела 0", html);
        // Руда, которую некому забрать, так и подписана — до первого расчёта без «+0 · −0».
        Assert.Contains("производит: Рудник · не потребляет ни одна ваша фабрика", html);
        // Свёрнутость переделов запоминается в браузере — у каждого свой ключ.
        Assert.Contains("data-collapse-key=", html);
    }

    [Fact]
    public async Task The_Needs_Board_Groups_Entries_By_Material_Inside_The_Team_Screen()
    {
        // Раньше доска была отдельной страницей без шапки и навигации, таблицей «одна запись — строка».
        var html = await RenderAsManager((session, teamId) =>
        {
            var ore = session.State.Config.Materials.Values.Single(m => m.Name == "Железная руда");
            session.PostNeed(teamId, ore.Id, NeedDirection.Surplus, NeedVolumeOrder.Large, "склад забит");
            return "/team/needs";
        });

        Assert.Contains("team-topbar", html);
        Assert.Contains("<a href=\"team/deals\" class=\"active\"", html); // доска — часть «Сделок»
        Assert.Contains("Железная руда", html);
        Assert.Contains(">предлагаем</span>", html);
        Assert.Contains("«склад забит»", html);
        Assert.Contains(">отозвать</button>", html);
    }

    private static string MineDefinitionId(GameSession session, Ulid teamId) => session.State.Config.FactoryDefinitions
        .First(d => d.Sector == session.State.Teams[teamId].Sector && d.Recipes[0].Output.Level == 0).Id;

    private static Task<string> RenderAsManager(string path) => RenderAsManager((_, _) => path);

    private static async Task<string> RenderAsManager(Func<GameSession, Ulid, string> preparePath)
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            host.SetDraftConfig(FixtureConfig());
            host.AddStagedTeam("Тета", host.DraftConfig.Sectors.First().Id);
            var team = host.StagedTeams.Single();
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Тета");
            host.StartSessionFromDraft();
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision
            var path = preparePath(host.Session!, team.Id);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        }
        finally
        {
            host.HardReset();
        }
    }
}
