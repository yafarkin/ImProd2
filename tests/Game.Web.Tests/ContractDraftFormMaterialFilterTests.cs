using System.Net;
using Game.Config.Loading;
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
            host.SetDraftConfig(CrossSectorFixture());

            var sectorA = host.DraftConfig.Sectors.Single(s => s.Id == "A").Id; // металлургия
            var sectorB = host.DraftConfig.Sectors.Single(s => s.Id == "B").Id; // нефтехимия
            host.AddStagedTeam("Дзета", sectorA);
            host.AddStagedTeam("Эта", sectorB);
            var team = host.StagedTeams.First(t => t.Name == "Дзета");
            var counterpartyTeam = host.StagedTeams.First(t => t.Name == "Эта");
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Дзета");
            host.AddStagedParticipant(ParticipantRole.Manager, counterpartyTeam.Id, "Управляющий Эта");
            host.StartSessionFromDraft();
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            // Фабрика сектора A, у которой есть вход из сектора Б — у команды появляется реальная
            // потребность в чужом материале. Контрагент («Эта», сектор Б) при этом ничего не строит:
            // оборот этого материала ему формально доступен по сектору, но фабрики нет, значит
            // продать нечего. Тип фабрики ищется по форме рецепта, а не по коду: коды принадлежат
            // контенту и меняются вместе с ним, а проверяемое здесь правило — общее.
            host.Session!.BuildFactory(team.Id, CrossSectorImporterOf(host.Session!.State.Config, "A", "B").Id);

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
            host.SetDraftConfig(CrossSectorFixture());

            var sectorA = host.DraftConfig.Sectors.Single(s => s.Id == "A").Id;
            var sectorB = host.DraftConfig.Sectors.Single(s => s.Id == "B").Id;
            host.AddStagedTeam("Тета", sectorA);
            host.AddStagedTeam("Йота", sectorB);
            var team = host.StagedTeams.First(t => t.Name == "Тета");
            var counterpartyTeam = host.StagedTeams.First(t => t.Name == "Йота");
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Тета");
            host.AddStagedParticipant(ParticipantRole.Manager, counterpartyTeam.Id, "Управляющий Йота");
            host.StartSessionFromDraft();
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            host.Session!.BuildFactory(team.Id, CrossSectorImporterOf(host.Session!.State.Config, "A", "B").Id); // нужны порода и нефть
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

    /// <summary>
    /// Мини-каталог с ОДНОЙ кросс-секторной связью на первом переделе
    /// (<c>tests/Fixtures/production-models/cross-sector-minimal.json</c>): прокат сектора А берёт
    /// нефть у сектора Б. Боевая модель для этого теста не годится по двум причинам: у неё все
    /// кросс-входы начинаются с уровня 2 и выше, то есть требуют разблокированного поколения,
    /// которого у свежесозданной команды нет; и её содержание меняется при каждой перекалибровке,
    /// а проверяемое здесь правило («в списке материалов только то, что контрагент реально
    /// производит») к содержанию цепочки отношения не имеет.
    /// </summary>
    private static ResolvedGameConfig CrossSectorFixture() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "cross-sector-minimal.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    /// <summary>Тип фабрики сектора <paramref name="sectorId"/>, у рецепта которого есть вход из сектора <paramref name="importFromSectorId"/>.</summary>
    private static FactoryDefinition CrossSectorImporterOf(ResolvedGameConfig config, string sectorId, string importFromSectorId) =>
        config.FactoryDefinitions.First(definition =>
            definition.Sector.Id == sectorId &&
            definition.Recipes.Any(recipe => recipe.Inputs.Any(input => input.Material.Sector.Id == importFromSectorId)));
}
