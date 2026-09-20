using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Вкладка «Требует внимания» на /team (docs/TODO.md №7) — проверяет, что страница реально отдаёт
/// её HTML: и пустое состояние, и настоящий повод со счётчиком. Сами правила отбора поводов покрыты
/// <c>TeamAttentionCalculatorTests</c>, подписи — <see cref="AttentionTextTests"/>; здесь только
/// сборка страницы. Изолированная фабрика + <c>HardReset</c> до и после — тот же приём и по той же
/// причине, что у <see cref="TeamPageFactoryOverviewTests"/> (общий физический <c>App_Data</c>).
/// </summary>
public class TeamPageAttentionTests
{
    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    private static async Task<string> RenderTeamPage(Action<GameSessionHost, Ulid> arrange)
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            host.SetDraftConfig(FixtureConfig());
            var sectorId = host.DraftConfig.Sectors.First().Id;
            host.AddStagedTeam("Дзета", sectorId);
            var team = host.StagedTeams.Single();
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Дзета");
            host.StartSessionFromDraft();
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            arrange(host, team.Id);

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

    /// <summary>
    /// Тишина — нормальное и частое состояние панели, и показана она обязана быть явно: панель,
    /// которая всегда что-то показывает, перестаёт читаться, а пустая без объяснения выглядит
    /// сломанной.
    /// </summary>
    [Fact]
    public async Task An_Untouched_Team_Sees_An_Explicit_All_Clear()
    {
        var html = await RenderTeamPage((_, _) => { });

        Assert.Contains("Требует внимания", html);
        Assert.Contains("Ничего срочного", html);
        Assert.DoesNotContain("badge rounded-pill bg-danger", html);
    }

    /// <summary>Фабрика построена, содержание списывается, рабочих нет — самый дешёвый способ незаметно терять деньги весь первый час.</summary>
    [Fact]
    public async Task A_Factory_Without_Workers_Shows_Up_On_The_Tab_With_A_Counter()
    {
        var html = await RenderTeamPage((host, teamId) =>
        {
            var mineDefinitionId = host.Session!.State.Config.FactoryDefinitions
                .Single(d => d.Sector.Id == host.Session!.State.Teams[teamId].Sector.Id && d.Recipes.Single().Output.Level == 0).Id;
            host.Session!.BuildFactory(teamId, mineDefinitionId);
        });

        Assert.Contains("нет рабочих", html);
        Assert.Contains("содержание фабрики списывается каждый ход", html);
        Assert.Contains("badge rounded-pill bg-danger", html);
        Assert.DoesNotContain("Ничего срочного", html);
    }
}
