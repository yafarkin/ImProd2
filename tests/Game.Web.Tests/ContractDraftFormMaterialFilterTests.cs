using System.Net;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// «Материал» в черновике сделки (<see cref="Game.Web.Components.Shared.ContractDraftForm"/>) —
/// ограничен тем, кто в сделке продавец, и тем, что реально производят его построенные фабрики
/// (запрос пользователя). Уточнение по живому логу: изначальная версия в роли покупателя брала
/// собственные потребности команды, не глядя на выбранного контрагента — металлургия с фабрикой
/// «концентрационный завод» (рецепт нуждается в нефти, отладочный конфиг, коммит про кросс-секторные
/// связи с нефтехимией) предлагала купить нефть даже у контрагента, который её вообще не добывает.
/// Тот же приём изоляции, что и у <see cref="TeamPageFactoryOverviewTests"/> (см. её doc-comment и
/// <see cref="AssemblyFixture"/>): своя фабрика приложения + <see cref="GameSessionHost.HardReset"/>
/// до и после, отладочный конфиг — единственный сэмпл с межсекторными входами, нужными для сценария.
/// </summary>
public class ContractDraftFormMaterialFilterTests
{
    [Fact]
    public async Task Negotiate_Page_Does_Not_Offer_To_Buy_A_Material_The_Counterparty_Cannot_Produce()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            host.SetDraftConfig(host.DebugConfig);

            var sectorA = host.DraftConfig.Sectors.Single(s => s.Id == "A").Id; // металлургия
            var sectorB = host.DraftConfig.Sectors.Single(s => s.Id == "B").Id; // нефтехимия
            host.AddStagedTeam("Дзета", sectorA);
            host.AddStagedTeam("Эта", sectorB);
            var team = host.StagedTeams.First(t => t.Name == "Дзета");
            var counterpartyTeam = host.StagedTeams.First(t => t.Name == "Эта");
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Дзета");
            host.AddStagedParticipant(ParticipantRole.Manager, counterpartyTeam.Id, "Управляющий Эта");
            var preset = host.DraftConfig.Raw.SessionPresets.Single();

            host.StartSessionFromDraft(preset);
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            // Концентрационный завод плавит железо из породы И нефти (отладочный конфиг, кросс-
            // секторная связь металлургии с нефтехимией) — у команды появляется реальная потребность
            // в нефти. Контрагент («Эта», сектор нефтехимии) при этом ничего не строит — оборот нефти
            // ей формально доступен по сектору, но фабрики нет, значит продать нечего.
            host.Session!.BuildFactory(team.Id, "concentration-plant");

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

            var response = await client.GetAsync("/team/negotiate");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            // Форма открывается в роли «Покупатель», единственный контрагент выбран по умолчанию.
            Assert.Contains("Контрагент ничего не производит", html);
            Assert.DoesNotContain("value=\"oil\"", html);
        }
        finally
        {
            host.HardReset();
        }
    }

    [Fact]
    public async Task Negotiate_Page_Offers_To_Buy_A_Material_The_Counterparty_Actually_Produces()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            host.SetDraftConfig(host.DebugConfig);

            var sectorA = host.DraftConfig.Sectors.Single(s => s.Id == "A").Id;
            var sectorB = host.DraftConfig.Sectors.Single(s => s.Id == "B").Id;
            host.AddStagedTeam("Тета", sectorA);
            host.AddStagedTeam("Йота", sectorB);
            var team = host.StagedTeams.First(t => t.Name == "Тета");
            var counterpartyTeam = host.StagedTeams.First(t => t.Name == "Йота");
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Тета");
            host.AddStagedParticipant(ParticipantRole.Manager, counterpartyTeam.Id, "Управляющий Йота");
            var preset = host.DraftConfig.Raw.SessionPresets.Single();

            host.StartSessionFromDraft(preset);
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            host.Session!.BuildFactory(team.Id, "concentration-plant"); // нужны порода и нефть
            host.Session!.BuildFactory(counterpartyTeam.Id, "oil-well"); // контрагент реально добывает нефть

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

            var response = await client.GetAsync("/team/negotiate");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Contains("value=\"oil\"", html);
        }
        finally
        {
            host.HardReset();
        }
    }
}
