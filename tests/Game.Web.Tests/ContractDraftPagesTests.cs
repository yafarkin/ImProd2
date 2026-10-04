using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Черновик переговорщика доходит до управляющего (SPEC §3). До этого черновик жил только в браузере
/// переговорщика: управляющий его не видел, и счётчика «ждёт вашего решения» не было вовсе
/// (<c>docs/manager-ui/README.md</c> §2 п.4 и п.20). Проверяется по отрендеренному HTML.
/// </summary>
public class ContractDraftPagesTests
{
    // Дробная цена намеренно: в боевой модели цены за единицу 0.07–0.6 ¤, и FormatMoney показал бы её как «0 ¤».
    private const decimal DraftUnitPrice = 0.47m;
    private const decimal DraftVolume = 4321m;

    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    [Fact]
    public async Task Manager_Sees_The_Negotiators_Draft_With_Its_Terms_And_A_Counter_In_The_Header()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var codes = StartTwoTeams(host);
            PrepareDraft(host.Session!, codes);

            var html = await RenderTeamPage(factory, codes.ManagerCode);

            // Черновик — в «Ждёт моего ответа» раздела «Сделки», с условиями и всеми тремя действиями.
            Assert.Contains("Ждёт моего ответа", html);
            Assert.Contains("Черновик от Переговорщик Ню: покупка у команды «Кси»", html);
            Assert.Contains("4321 ед. × 0.47 ¤", html);
            Assert.Contains(">Подать заявку</button>", html);
            Assert.Contains(">Поправить</button>", html);
            Assert.Contains(">Вернуть</button>", html);
            // Счётчик из SPEC §3 — в шапке, виден из любого раздела.
            Assert.Matches(@"Ждёт меня\s*<span[^>]*>1</span>", html);
        }
        finally
        {
            host.HardReset();
        }
    }

    [Fact]
    public async Task The_Counterparty_Learns_Nothing_About_A_Draft()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var codes = StartTwoTeams(host);
            PrepareDraft(host.Session!, codes);

            var html = await RenderTeamPage(factory, codes.CounterpartyManagerCode);

            // Имя переговорщика тут не проверяется: в отладочном режиме оно есть в переключателе участников.
            // Видимый текст, а не HTML: в атрибутах есть случайные Ulid, где «4321» изредка встречается само.
            Assert.DoesNotContain("4321", System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " "));
            Assert.Contains("Ничего не ждёт", html);
        }
        finally
        {
            host.HardReset();
        }
    }

    [Fact]
    public async Task Negotiator_Sees_That_The_Manager_Returned_The_Draft_And_Why()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var codes = StartTwoTeams(host);
            PrepareDraft(host.Session!, codes);
            var draftId = host.Session!.State.ContractDrafts.Keys.Single();
            host.Session.ReturnContractDraft(draftId, codes.TeamId, "слишком дорого");

            var html = await RenderTeamPage(factory, codes.NegotiatorCode);

            Assert.Contains("Мои черновики", html);
            Assert.Contains("возвращён", html);
            Assert.Contains("Причина: слишком дорого", html);
        }
        finally
        {
            host.HardReset();
        }
    }

    private sealed record Codes(Ulid TeamId, Ulid CounterpartyId, string ManagerCode, string CounterpartyManagerCode, string NegotiatorCode);

    private static Codes StartTwoTeams(GameSessionHost host)
    {
        host.SetDraftConfig(FixtureConfig());
        var sectorId = host.DraftConfig.Sectors.First().Id;
        host.AddStagedTeam("Ню", sectorId);
        host.AddStagedTeam("Кси", sectorId);

        var team = host.StagedTeams.First(t => t.Name == "Ню");
        var counterparty = host.StagedTeams.First(t => t.Name == "Кси");
        var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Ню");
        var counterpartyManager = host.AddStagedParticipant(ParticipantRole.Manager, counterparty.Id, "Управляющий Кси");
        var negotiator = host.AddStagedParticipant(ParticipantRole.Negotiator, team.Id, "Переговорщик Ню");

        host.StartSessionFromDraft();
        host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

        return new Codes(team.Id, counterparty.Id, manager.Code, counterpartyManager.Code, negotiator.Code);
    }

    private static void PrepareDraft(GameSession session, Codes codes)
    {
        var material = session.State.Config.Materials.Values.First();
        var terms = new ContractTerms(
            ContractType.Spot, material, DraftVolume, DraftUnitPrice,
            penaltyRate: 0.1m, effectiveTurn: 1, spotDeliveryTurn: 5, recurringEndTurn: null);

        session.PrepareContractDraft(new ContractProposal(codes.TeamId, codes.CounterpartyId, codes.TeamId, terms), codes.NegotiatorCode);
    }

    private static async Task<string> RenderTeamPage(WebApplicationFactory<Program> factory, string loginCode)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = loginCode }));

        var response = await client.GetAsync("/team/deals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
