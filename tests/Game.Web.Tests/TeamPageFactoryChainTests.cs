using System.Net;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Диаграмма цепочки фабрик сектора на /team (запрос пользователя «не понятно с чего начинать») —
/// проверяет, что страница реально отдаёт SVG с построенными и непостроенными узлами, а не просто
/// что <see cref="FactoryChainDiagram.Build"/> сама по себе верна (это уже покрыто отдельными
/// юнит-тестами). Изолированная фабрика + <see cref="GameSessionHost.HardReset"/> — тот же приём,
/// что и у соседних тестов черновика/сессии в AuthenticationTests.cs. Дополнительно чистит за собой
/// в конце (второй <c>HardReset()</c>) — этот тест единственный в проекте реально стартует сессию
/// вне <c>AuthenticationTests</c>' общей fixture, а её конструктор считает себя первым и сеет
/// тестовую сессию только если <c>Host.Session is null</c>; оставленная этим тестом на диске
/// незавершённая сессия иначе просачивается в общую fixture, если тестовые классы этого проекта
/// (все делят один физический <c>App_Data</c>, см. <see cref="AssemblyFixture"/>) выполнятся в другом порядке.
/// </summary>
public class TeamPageFactoryChainTests
{
    [Fact]
    public async Task Team_Page_Renders_Built_And_Unbuilt_Factory_Nodes_With_A_Connecting_Edge()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var sectorId = host.DefaultConfig.Sectors.First().Id;
            host.AddStagedTeam("Дельта", sectorId);
            var team = host.StagedTeams.Single();
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Дельта");
            var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");

            host.StartSessionFromDraft(preset);
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            // Строим только рудник (уровень 0) — сталелитейный завод и прокатный стан сектора A
            // остаются непостроенными, но должны появиться на диаграмме пунктиром со связью от рудника.
            var mineDefinitionId = host.Session!.State.Config.FactoryDefinitions
                .Single(d => d.Sector.Id == sectorId && d.Recipes.Single().Output.Level == 0).Id;
            host.Session!.BuildFactory(team.Id, mineDefinitionId);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

            var response = await client.GetAsync("/team");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("factory-chain-arrow", html);
            Assert.Contains("id=\"decide-now\"", html);
            Assert.Contains($"factory-card-{host.Session!.State.Teams[team.Id].Factories.Single().Id}", html);
            Assert.Contains("stroke-dasharray", html); // хотя бы один непостроенный узел
            Assert.Contains("не построена", html);
        }
        finally
        {
            host.HardReset();
        }
    }

    /// <summary>Запрос пользователя «я хочу построить столько фабрик, сколько хочу» — второй экземпляр одного типа больше не запрещён ни движком, ни формой на /team.</summary>
    [Fact]
    public async Task Team_Page_Lets_A_Manager_Build_A_Second_Factory_Of_The_Same_Type()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var sectorId = host.DefaultConfig.Sectors.First().Id;
            host.AddStagedTeam("Эпсилон", sectorId);
            var team = host.StagedTeams.Single();
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Эпсилон");
            var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");

            host.StartSessionFromDraft(preset);
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            var mineDefinitionId = host.Session!.State.Config.FactoryDefinitions
                .Single(d => d.Sector.Id == sectorId && d.Recipes.Single().Output.Level == 0).Id;
            host.Session!.BuildFactory(team.Id, mineDefinitionId);
            host.Session!.BuildFactory(team.Id, mineDefinitionId); // не должно бросить — второй экземпляр того же типа

            Assert.Equal(2, host.Session!.State.Teams[team.Id].Factories.Count);

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

            var response = await client.GetAsync("/team");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("построено: 2", html);
        }
        finally
        {
            host.HardReset();
        }
    }
}
