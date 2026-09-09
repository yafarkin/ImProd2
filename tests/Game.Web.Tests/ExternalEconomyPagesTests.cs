using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Критерий готовности блока 11.10 (<c>docs/external-economy.md</c> §9): поверхности внешней
/// экономики реально отдаются страницами, а не только верны сами по себе в юнит-тестах отображения
/// (<see cref="EconomyIndexDisplayTests"/>, <see cref="MarketSalePreviewTests"/>). Ручной прогон
/// такое подтверждает один раз, тест — каждый раз.
///
/// <para>Приём с изолированной фабрикой и <see cref="GameSessionHost.HardReset"/> до и после — тот
/// же, что в <see cref="TeamPageFactoryOverviewTests"/>: этот класс тоже стартует свою сессию и
/// обязан не оставлять её на общем диске.</para>
/// </summary>
public class ExternalEconomyPagesTests
{
    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    /// <summary>
    /// Поставляемый сессионный файл уже на экзогенной модели (блок 11.8) — если это перестанет быть
    /// так, индекс исчезнет из интерфейса по <see cref="EconomyIndexDisplay.IsVisible"/>, и
    /// проверки ниже должны падать осмысленно, а не «просто не найти строку».
    /// </summary>
    [Fact]
    public void The_Shipped_Session_Really_Runs_The_External_Model()
    {
        Assert.True(EconomyIndexDisplay.IsVisible(FixtureConfig().Raw.Economy));
    }

    [Fact]
    public async Task The_Big_Screen_Shows_The_Economy_Index_With_Its_History()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            StartSessionWithOneTeam(host, "Гамма");

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var response = await client.GetAsync("/screen");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Contains("Индекс деловой активности", html);
            Assert.Contains("нейтральн", html); // подпись уровня относительно нейтрального
        }
        finally
        {
            host.HardReset();
        }
    }

    [Fact]
    public async Task The_Team_Dashboard_Shows_The_Index_And_The_Sale_Preview()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var manager = StartSessionWithOneTeam(host, "Йота");

            // Ход на расчёт: рудник успевает произвести руду, и на складе появляется то, для чего
            // предпросмотр продажи вообще имеет смысл. Возвращаемся в «Решения» — только в этой фазе
            // управляющий видит форму продажи с предпросмотром.
            AdvanceTo(host.Session!, TurnPhase.Settlement);
            host.Session!.RunTick(new Random(1));
            AdvanceTo(host.Session!, TurnPhase.Decision);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

            var response = await client.GetAsync("/team");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Contains("Индекс деловой активности", html);
            Assert.Contains("Цена за единицу", html);
        }
        finally
        {
            host.HardReset();
        }
    }

    private static void AdvanceTo(GameSession session, TurnPhase phase)
    {
        while (session.State.CurrentPhase != phase)
        {
            session.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        }
    }

    private static StagedParticipantSpec StartSessionWithOneTeam(GameSessionHost host, string teamName)
    {
        host.SetDraftConfig(FixtureConfig());
        var sectorId = host.DraftConfig.Sectors.First().Id;
        host.AddStagedTeam(teamName, sectorId);
        var team = host.StagedTeams.Single();
        var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий " + teamName);
        host.StartSessionFromDraft();
        host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

        var mineDefinitionId = host.Session!.State.Config.FactoryDefinitions
            .Single(d => d.Sector.Id == sectorId && d.Recipes.Single().Output.Level == 0).Id;
        host.Session!.BuildFactory(team.Id, mineDefinitionId);
        host.Session!.SetWorkerCount(team.Id, host.Session!.State.Teams[team.Id].Factories.Single().Id, 5);

        return manager;
    }
}
