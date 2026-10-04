using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Главное свойство двухстороннего ввода условий (SPEC §6, <c>docs/TODO.md</c> №16): условия чужой
/// заявки не доходят до контрагента вовсе. Это не косметика экрана, а вся механика целиком — если
/// числа видны, встречный ввод вырождается обратно в копирование готовых условий, ради чего пункт и
/// заводился. Проверяется по отрендеренному HTML, а не по модели строки: утечь может именно разметка.
/// </summary>
public class ContractProposalPrivacyTests
{
    // Значения намеренно круглые и ни на что не похожие: цена проходит через FormatMoney ("N0",
    // округление до целого), поэтому дробное 777,77 отрисовалось бы как 778 и тест ловил бы не то.
    private const decimal DistinctiveUnitPrice = 777m;
    private const decimal DistinctiveVolume = 1234m;

    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    [Fact]
    public async Task Counterparty_Sees_That_A_Proposal_Exists_But_Never_Its_Numbers()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var (author, counterparty, authorManagerCode, counterpartyManagerCode, _) = StartTwoTeams(host);
            SubmitProposal(host.Session!, author, counterparty);

            var counterpartyHtml = await RenderTeamPage(factory, counterpartyManagerCode);

            // Факт заявки виден — иначе непонятно, к кому идти договариваться.
            Assert.Contains("подала заявку на сделку", counterpartyHtml);
            Assert.Contains("Условия скрыты — их назовут лично", counterpartyHtml);

            // Сами условия — нет. Проверяем видимый текст: в атрибутах страницы есть случайные Ulid, и
            // «777» или «1234» в них изредка попадается само по себе.
            Assert.DoesNotContain("777", VisibleText(counterpartyHtml));
            Assert.DoesNotContain("1234", VisibleText(counterpartyHtml));

            // У автора его собственные условия при этом на экране есть.
            var authorHtml = await RenderTeamPage(factory, authorManagerCode);
            Assert.Contains("777", authorHtml);
            Assert.Contains("1234", authorHtml);
        }
        finally
        {
            host.HardReset();
        }
    }

    /// <summary>
    /// «Ответить заявкой» (блок 4 редизайна) предзаполняет форму контрагентом, стороной и материалом —
    /// но не условиями: их по-прежнему называют лично, иначе сверка выродилась бы в копирование (№16).
    /// </summary>
    [Fact]
    public async Task Replying_To_A_Proposal_Prefills_The_Parties_But_Never_The_Numbers()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var (author, counterparty, _, counterpartyManagerCode, _) = StartTwoTeams(host);
            SubmitProposal(host.Session!, author, counterparty);
            var proposalId = host.Session!.State.ContractProposals.Keys.Single();

            var html = await RenderTeamPage(factory, counterpartyManagerCode, $"/team/deals?reply={proposalId}");

            Assert.Matches("Ответ на заявку команды <strong[^>]*>«Ню»</strong>", html);
            Assert.Matches("<option value=\"buyer\"[^>]*selected>Покупаем</option>", html); // заявку подали нам как продавцу
            Assert.DoesNotContain("777", VisibleText(html));
            Assert.DoesNotContain("1234", VisibleText(html));
        }
        finally
        {
            host.HardReset();
        }
    }

    [Fact]
    public async Task Negotiator_Is_Told_That_Only_The_Manager_Submits_A_Proposal()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            // Переговорщик заводится до старта, тем же путём, что и управляющие.
            var negotiatorCode = StartTwoTeams(host, withNegotiator: true).NegotiatorCode!;

            var html = await RenderTeamPage(factory, negotiatorCode);

            Assert.Contains("заявку контрагенту подаёт он", html);
            Assert.Contains("Передать управляющему", html);
        }
        finally
        {
            host.HardReset();
        }
    }

    private static (Ulid Author, Ulid Counterparty, string AuthorManagerCode, string CounterpartyManagerCode, string? NegotiatorCode)
        StartTwoTeams(GameSessionHost host, bool withNegotiator = false)
    {
        host.SetDraftConfig(FixtureConfig());
        var sectorId = host.DraftConfig.Sectors.First().Id;
        host.AddStagedTeam("Ню", sectorId);
        host.AddStagedTeam("Кси", sectorId);

        var author = host.StagedTeams.First(t => t.Name == "Ню");
        var counterparty = host.StagedTeams.First(t => t.Name == "Кси");
        var authorManager = host.AddStagedParticipant(ParticipantRole.Manager, author.Id, "Управляющий Ню");
        var counterpartyManager = host.AddStagedParticipant(ParticipantRole.Manager, counterparty.Id, "Управляющий Кси");
        var negotiator = withNegotiator
            ? host.AddStagedParticipant(ParticipantRole.Negotiator, author.Id, "Переговорщик Ню")
            : null;

        host.StartSessionFromDraft();
        host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision

        return (author.Id, counterparty.Id, authorManager.Code, counterpartyManager.Code, negotiator?.Code);
    }

    private static void SubmitProposal(GameSession session, Ulid authorId, Ulid counterpartyId)
    {
        var material = session.State.Config.Materials.Values.First();
        var terms = new ContractTerms(
            ContractType.Spot, material, DistinctiveVolume, DistinctiveUnitPrice,
            penaltyRate: 0.1m, effectiveTurn: 1, spotDeliveryTurn: 5, recurringEndTurn: null);

        session.SubmitContractProposal(
            new ContractProposal(counterpartyId, authorId, authorId, terms), TeamRole.Manager, new Random(1));
    }

    /// <summary>Текст страницы без тегов и атрибутов — то, что видит игрок.</summary>
    private static string VisibleText(string html) => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ");

    private static async Task<string> RenderTeamPage(WebApplicationFactory<Program> factory, string loginCode, string path = "/team/deals")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = loginCode }));

        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
