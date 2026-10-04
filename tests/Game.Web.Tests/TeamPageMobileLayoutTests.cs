using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Мобильная вёрстка экрана команды (docs/TODO.md №7). Участники заходят с личных телефонов, и до
/// правок 2026-09-08 страница на 390px теряла половину интерфейса: из семи вкладок было видно
/// четыре, без единого намёка на остальные, а таблицы уезжали за край экрана.
///
/// <para>
/// Проверяется разметка, а не картинка: тест не умеет измерять экран, но умеет сторожить те два
/// решения, которые эту проблему и чинят, — вкладки переносятся вместо горизонтальной прокрутки, а
/// второстепенные колонки таблиц помечены как скрываемые на узком экране. Обе правки выглядят как
/// «лишние» классы и первыми пойдут под нож при следующей переделке разметки, если их ничем не
/// держать.
/// </para>
/// </summary>
public class TeamPageMobileLayoutTests
{
    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    private static async Task<string> RenderTeamPage(string path)
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            host.SetDraftConfig(FixtureConfig());
            var sectorId = host.DraftConfig.Sectors.First().Id;
            host.AddStagedTeam("Эта", sectorId);
            var team = host.StagedTeams.Single();
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Эта");
            host.StartSessionFromDraft();
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            var mineDefinitionId = host.Session!.State.Config.FactoryDefinitions
                .Single(d => d.Sector.Id == sectorId && d.Recipes.Single().Output.Level == 0).Id;
            host.Session!.BuildFactory(team.Id, mineDefinitionId);

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

    /// <summary>
    /// Семь вкладок страницы заменила навигация из четырёх разделов с собственными адресами (блок 1
    /// редизайна, docs/manager-ui/README.md §8): на телефоне это нижняя панель, где все четыре видны
    /// всегда. До неё из семи вкладок на 390px было видно четыре, без признака, что есть остальные.
    /// </summary>
    [Fact]
    public async Task All_Four_Sections_Are_Reachable_From_Any_Section()
    {
        var html = await RenderTeamPage("/team/analytics");

        Assert.Contains("team-nav", html);
        Assert.Contains("href=\"team/production\"", html);
        Assert.Contains("href=\"team/deals\"", html);
        Assert.Contains("href=\"team/analytics\"", html);
        Assert.Contains("href=\"team\"", html);
    }

    /// <summary>
    /// У фабрики больше нет вкладок, которые на 390px уезжали за край: с блока 3 редизайна она —
    /// отдельная страница со всеми рычагами подряд (docs/manager-ui/README.md §4). Сторожим, чтобы
    /// вкладки с горизонтальной прокруткой не вернулись ни в «Производство», ни на страницу фабрики.
    /// </summary>
    [Fact]
    public async Task Production_Has_No_Sideways_Scrolling_Tabs()
    {
        var html = await RenderTeamPage("/team/production");

        Assert.DoesNotContain("nav nav-pills", html);
        Assert.DoesNotContain("overflow-x:auto; white-space:nowrap", html);
    }

    /// <summary>
    /// В истории операций точное время и ставка скрыты на узком экране: без этого таблица не влезала
    /// по ширине даже с уменьшенным кеглем, а ход и сумма — то, ради чего в неё и смотрят с телефона.
    /// </summary>
    [Fact]
    public async Task Finance_History_Hides_Low_Value_Columns_On_Narrow_Screens()
    {
        var html = await RenderTeamPage("/team/analytics");

        Assert.Contains("<th class=\"d-none d-md-table-cell\">Время</th>", html);
        Assert.Contains("<th class=\"d-none d-md-table-cell\">Ставка</th>", html);
        // Ход и сумма скрываться не должны ни при какой ширине — на них таблица и держится.
        Assert.Contains("<th>Ход</th>", html);
        Assert.Contains("<th>Сумма</th>", html);
    }
}
