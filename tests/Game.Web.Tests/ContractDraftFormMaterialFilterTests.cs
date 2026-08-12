using System.Net;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// «Материал» в черновике сделки (<see cref="Game.Web.Components.Shared.ContractDraftForm"/>, запрос
/// пользователя) — не весь каталог материалов сессии, а только то, что реально относится к
/// построенным фабрикам команды: в роли покупателя — входы выбранных рецептов, в роли продавца — их
/// выход. Тот же приём изоляции, что и у <see cref="TeamPageFactoryOverviewTests"/> (см. её
/// doc-comment и <see cref="AssemblyFixture"/>): своя фабрика приложения + <see
/// cref="GameSessionHost.HardReset"/> до и после.
/// </summary>
public class ContractDraftFormMaterialFilterTests
{
    [Fact]
    public async Task Negotiate_Page_Hides_The_Buyer_Material_Picker_When_The_Only_Built_Factory_Needs_No_Inputs()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            // Второй, ничем не занятой команды хватает — форма прячет весь выбор материала за
            // «Нет других команд для сделки», пока контрагентов вообще нет.
            var sectorId = host.DefaultConfig.Sectors.First().Id;
            host.AddStagedTeam("Дзета", sectorId);
            host.AddStagedTeam("Эта", sectorId);
            var team = host.StagedTeams.First(t => t.Name == "Дзета");
            var otherTeam = host.StagedTeams.First(t => t.Name == "Эта");
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Дзета");
            host.AddStagedParticipant(ParticipantRole.Manager, otherTeam.Id, "Управляющий Эта");
            var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");

            host.StartSessionFromDraft(preset);
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            // Рудник (уровень 0) добывает руду без сырьевых входов — команде с одним таким рудником
            // нечего покупать: раньше форма всё равно предлагала весь каталог материалов сессии.
            var mineDefinitionId = host.Session!.State.Config.FactoryDefinitions
                .Single(d => d.Sector.Id == sectorId && d.Recipes.Single().Output.Level == 0).Id;
            host.Session!.BuildFactory(team.Id, mineDefinitionId);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

            var response = await client.GetAsync("/team/negotiate");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            // Форма открывается в роли «Покупатель» по умолчанию — рудник без входов даёт пустой список.
            Assert.Contains("Нечего покупать", html);
        }
        finally
        {
            host.HardReset();
        }
    }
}
