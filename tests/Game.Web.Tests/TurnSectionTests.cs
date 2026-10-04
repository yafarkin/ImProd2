using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Раздел «Ход» (docs/manager-ui/README.md §8, блок 2): итоги расчёта, «Ждёт моего решения» и «Приказ на
/// ход» на живой сессии, по отрендеренному HTML.
/// </summary>
public class TurnSectionTests
{
    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    [Fact]
    public async Task A_Declared_Hire_Shows_Up_In_The_Order_For_The_Turn()
    {
        var html = await RenderTurn((session, ourTeam, _) =>
        {
            var mine = BuildMine(session, ourTeam);
            session.SetWorkerCount(ourTeam, mine, 5);
        });

        Assert.Contains("Приказ на ход 1", html);
        Assert.Contains("нанять 5", html);
        Assert.DoesNotContain("Решений на этот ход пока нет", html);
    }

    [Fact]
    public async Task An_Incoming_Proposal_Waits_For_The_Manager_Without_Its_Numbers()
    {
        var html = await RenderTurn((session, ourTeam, otherTeam) =>
        {
            var material = session.State.Config.Materials.Values.First();
            var terms = new ContractTerms(ContractType.Spot, material, 4321m, 777m, 0.1m, 1, 5, null);
            session.SubmitContractProposal(new ContractProposal(ourTeam, otherTeam, otherTeam, terms), TeamRole.Manager, new Random(1));
        });

        Assert.Contains("Ждёт моего решения", html);
        Assert.Contains("подала заявку на сделку", html);
        Assert.Contains("Ответить заявкой", html);
        Assert.DoesNotContain("4321", html);
        Assert.Matches(@"Ждёт меня\s*<span[^>]*>1</span>", html);
    }

    [Fact]
    public async Task After_A_Real_Settlement_The_Turn_Shows_Its_Results()
    {
        var html = await RenderTurn((session, ourTeam, _) =>
        {
            var mine = BuildMine(session, ourTeam);
            session.SetWorkerCount(ourTeam, mine, 5);
            PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1)); // Решения(1) -> Расчёт(2), с расчётом
            PhaseAutoAdvancer.AdvanceByFacilitator(session, new Random(1)); // Расчёт(2) -> Решения(2)
        });

        Assert.Contains("Итоги расчёта хода 2", html);
        Assert.Contains("Зарплаты и содержание", html);
        Assert.Contains("Произведено:", html);
    }

    [Fact]
    public async Task Styles_And_Scripts_Carry_The_Build_Version()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        // Без метки телефон держал старый файл стилей из кеша и показывал новую разметку без оформления.
        Assert.Contains($"app.css?v={AssetVersion.Value}", html);
        Assert.Contains($"Game.Web.styles.css?v={AssetVersion.Value}", html);
        Assert.Contains($"js/team.js?v={AssetVersion.Value}", html);
    }

    private static Ulid BuildMine(GameSession session, Ulid teamId)
    {
        var mineDefinitionId = session.State.Config.FactoryDefinitions
            .First(d => d.Sector == session.State.Teams[teamId].Sector && d.Recipes[0].Output.Level == 0).Id;
        session.BuildFactory(teamId, mineDefinitionId);
        return session.State.Teams[teamId].Factories.Single().Id;
    }

    private static async Task<string> RenderTurn(Action<GameSession, Ulid, Ulid> prepare)
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            host.SetDraftConfig(FixtureConfig());
            var sectorId = host.DraftConfig.Sectors.First().Id;
            host.AddStagedTeam("Каппа", sectorId);
            host.AddStagedTeam("Лямбда", sectorId);
            var ourTeam = host.StagedTeams.First(t => t.Name == "Каппа");
            var otherTeam = host.StagedTeams.First(t => t.Name == "Лямбда");
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, ourTeam.Id, "Управляющий Каппа");
            host.AddStagedParticipant(ParticipantRole.Manager, otherTeam.Id, "Управляющий Лямбда");
            host.StartSessionFromDraft();
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision
            prepare(host.Session!, ourTeam.Id, otherTeam.Id);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

            var response = await client.GetAsync("/team");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        }
        finally
        {
            host.HardReset();
        }
    }
}
