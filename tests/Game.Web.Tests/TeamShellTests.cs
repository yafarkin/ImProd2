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
    [InlineData("/team/production", "Построить фабрику")]
    [InlineData("/team/deals", "Черновик сделки")]
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

        Assert.DoesNotContain("Построить фабрику", html);
        Assert.DoesNotContain("Черновик сделки", html);
    }

    [Fact]
    public async Task The_Team_Roster_Lives_Behind_The_Menu_Not_On_The_Main_Section()
    {
        var html = await RenderAsManager("/team");

        Assert.DoesNotContain("Добавить переговорщика", html);
        Assert.Contains("href=\"/team/members\"", html);
    }

    [Fact]
    public async Task The_Factory_Has_A_Separate_Page_And_Opens_The_Requested_Instance()
    {
        // Так «Требует внимания» ведёт к фабрике: разделы не делят экземпляр страницы, и какую фабрику
        // открыть, передаётся через адрес. Список производства карточку больше не открывает.
        string? firstCard = null;
        string? secondCard = null;
        var html = await RenderAsManager((session, teamId) =>
        {
            var mineDefinitionId = session.State.Config.FactoryDefinitions
                .First(d => d.Sector == session.State.Teams[teamId].Sector && d.Recipes[0].Output.Level == 0).Id;
            session.BuildFactory(teamId, mineDefinitionId);
            session.BuildFactory(teamId, mineDefinitionId);
            var factories = session.State.Teams[teamId].Factories.OrderBy(f => f.Id).ToList();
            firstCard = $"factory-card-{factories[0].Id}";
            secondCard = $"factory-card-{factories[1].Id}";
            return $"/team/factory/{factories[1].Id}?tab=wear";
        });

        Assert.Contains(secondCard!, html);
        Assert.DoesNotContain(firstCard!, html);
        Assert.Contains("← Цепочка производства", html);
        Assert.Contains("ступень R&D", html);
        Assert.DoesNotContain("Цепочка производства</h2>", html);
        Assert.Contains("id=\"factory-wear\"", html);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("missing")]
    [InlineData("foreign")]
    public async Task A_Factory_Outside_The_Team_Does_Not_Show_Another_Factory(string kind)
    {
        var html = await RenderAsManager((session, teamId) =>
        {
            var definition = session.State.Config.FactoryDefinitions.First(d => d.Sector == session.State.Teams[teamId].Sector && d.Recipes[0].Output.Level == 0);
            session.BuildFactory(teamId, definition.Id);
            var id = Ulid.NewUlid().ToString();
            if (kind == "foreign")
            {
                var other = session.State.Teams.Values.First(t => t.Id != teamId);
                id = ((FactoryBuilt)session.BuildFactory(other.Id, definition.Id).Change).FactoryId.ToString();
            }
            return $"/team/factory/{(kind == "invalid" ? "garbage" : id)}";
        });
        Assert.Contains("Фабрика не найдена в вашей команде", html);
        Assert.DoesNotContain("id=\"factory-workers\"", html);
    }

    [Fact]
    public async Task Negotiators_See_The_Factory_Without_Management_Actions()
    {
        var html = await RenderAsManager((session, teamId) =>
        {
            var definition = session.State.Config.FactoryDefinitions.First(d => d.Sector == session.State.Teams[teamId].Sector && d.Recipes[0].Output.Level == 0);
            var id = ((FactoryBuilt)session.BuildFactory(teamId, definition.Id).Change).FactoryId;
            return $"/team/factory/{id}";
        }, ParticipantRole.Negotiator);
        Assert.Contains("Рабочих:", html);
        Assert.DoesNotContain("Применить</button>", html);
        Assert.DoesNotContain("Продать за", html);
    }

    private static Task<string> RenderAsManager(string path) => RenderAsManager((_, _) => path);

    private static async Task<string> RenderAsManager(Func<GameSession, Ulid, string> preparePath, ParticipantRole role = ParticipantRole.Manager)
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            host.SetDraftConfig(FixtureConfig());
            host.AddStagedTeam("Тета", host.DraftConfig.Sectors.First().Id);
            var team = host.StagedTeams.Single();
            host.AddStagedTeam("Другая", host.DraftConfig.Sectors.First().Id);
            var manager = host.AddStagedParticipant(role, team.Id, "Участник Тета");
            host.AddStagedParticipant(ParticipantRole.Manager, host.StagedTeams.First(t => t.Id != team.Id).Id, "Другой управляющий");
            if (role != ParticipantRole.Manager)
                host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Тета");
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
