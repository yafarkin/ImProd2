using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Подача инертного найма на карточке фабрики (docs/TODO.md №25). Механика растягивает наём на
/// несколько ходов, и без явного предупреждения объявленные 20 против нанятых 5 выглядят как сбой —
/// игрок обязан заранее видеть, сколько человек выйдет на смену уже в этот ход, а сколько потом.
///
/// <para>
/// Правило «сколько выйдет сейчас» продублировано в разметке (<c>HiresThisTurn</c>) отдельно от
/// движка (<see cref="WorkforceStep"/>) — у строки экрана нет доменной <c>Factory</c>. Дублирование
/// маленькое, но разъехаться может, поэтому тест проверяет именно совпадение цифры на экране с тем,
/// что реально спишет расчёт.
/// </para>
/// </summary>
public class TeamPageHiringRampTests
{
    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    private static async Task<string> RenderWithDeclaredWorkers(int declared, int recipeOutputLevel)
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            host.SetDraftConfig(FixtureConfig());
            var sectorId = host.DraftConfig.Sectors.First().Id;
            host.AddStagedTeam("Мю", sectorId);
            var team = host.StagedTeams.Single();
            var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Мю");
            host.StartSessionFromDraft();
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

            var definitionId = host.Session!.State.Config.FactoryDefinitions
                .First(d => d.Sector.Id == sectorId && d.Recipes.Any(r => r.Output.Level == recipeOutputLevel)).Id;
            var built = (FactoryBuilt)host.Session!.BuildFactory(team.Id, definitionId).Change;
            host.Session!.SetWorkerCount(team.Id, built.FactoryId, declared);

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

    /// <summary>Передел: объявлено больше предела — на экране «наймёт N из M» и обещание добрать остальных.</summary>
    [Fact]
    public async Task A_Processing_Factory_Warns_That_Only_Part_Of_The_Crew_Arrives_This_Turn()
    {
        var maxHiresPerTurn = FixtureConfig().Raw.WorkerProductivity.MaxHiresPerTurn;
        var declared = maxHiresPerTurn + 7;

        var html = await RenderWithDeclaredWorkers(declared, recipeOutputLevel: 1);

        Assert.Contains($"{maxHiresPerTurn} из {declared}", html);
        Assert.Contains($"Остальные {declared - maxHiresPerTurn} выйдут на смену в следующие ходы", html);
        Assert.Contains("объявлять заново не нужно", html);
    }

    /// <summary>Добыча нанимает мгновенно — растянутой формулировки быть не должно, она бы там врала.</summary>
    [Fact]
    public async Task Raw_Extraction_Says_The_Whole_Crew_Arrives_At_Once()
    {
        var declared = FixtureConfig().Raw.WorkerProductivity.MaxHiresPerTurn + 7;

        var html = await RenderWithDeclaredWorkers(declared, recipeOutputLevel: 0);

        Assert.Contains("здесь бригада выходит на смену сразу", html);
        // Ни одного признака растянутого найма: и «N из M», и обещание добрать остальных здесь были
        // бы прямой ложью — добыча укомплектовывается целиком на ближайшем расчёте.
        Assert.DoesNotContain("выйдут на смену в следующие ходы", html);
        Assert.DoesNotContain($" из {declared}", html);
    }
}
