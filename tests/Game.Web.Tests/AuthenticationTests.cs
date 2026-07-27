using System.Net;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>Вход по коду и разграничение доступа по роли (Блок 8.1, SPEC §3; сев сессии — Блок 9.8).</summary>
public class AuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Dictionary<ParticipantRole, string> SeedCodes = new();

    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;

        var host = _factory.Services.GetRequiredService<GameSessionHost>();
        if (host.Session is null)
        {
            SeedTestSession(host);
        }
    }

    /// <summary>
    /// Зеркалит прежний хардкодный сев <c>GameSessionHost</c> (Блок 8.1), но идёт через настоящий
    /// админский API (Блок 9.8) — конструктор вызывается заново на каждый тест-метод, а сессию
    /// заводит только первый вызов (общий на класс <see cref="GameSessionHost"/> из
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> уже стартовал бы к этому моменту).
    /// </summary>
    private static void SeedTestSession(GameSessionHost host)
    {
        var config = host.DefaultConfig;
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");
        var alphaId = Ulid.NewUlid();
        var betaId = Ulid.NewUlid();

        var teams = new[]
        {
            new TeamSpec { Id = alphaId, Name = "Альфа", SectorId = sectorA.Id, StartingLoanAmount = 10_000m },
            new TeamSpec { Id = betaId, Name = "Бета", SectorId = sectorB.Id, StartingLoanAmount = 10_000m },
        };
        var preset = config.Raw.SessionPresets.Single(p => p.Id == "short");

        host.StartNewSession(config, preset, teams);

        Register(host, ParticipantRole.Manager, alphaId, "Управляющий Альфа");
        Register(host, ParticipantRole.Negotiator, alphaId, "Переговорщик Альфа");
        Register(host, ParticipantRole.Manager, betaId, "Управляющий Бета");
        Register(host, ParticipantRole.Negotiator, betaId, "Переговорщик Бета");
        Register(host, ParticipantRole.Operator, null, "Оператор");
        Register(host, ParticipantRole.Facilitator, null, "Ведущий");
        Register(host, ParticipantRole.Administrator, null, "Администратор");
    }

    private static void Register(GameSessionHost host, ParticipantRole role, Ulid? teamId, string displayName)
    {
        var entry = host.RegisterParticipant(role, teamId, displayName);
        var registered = (ParticipantRegistered)entry.Change;
        SeedCodes.TryAdd(role, registered.Code);
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string SeedCodeFor(ParticipantRole role) => SeedCodes[role];

    private static Task<HttpResponseMessage> PostLogin(HttpClient client, string code) =>
        client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code }));

    [Fact]
    public async Task Login_With_A_Valid_Manager_Code_Redirects_To_Team_And_Sets_The_Auth_Cookie()
    {
        var client = CreateClient();

        var response = await PostLogin(client, SeedCodeFor(ParticipantRole.Manager));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/team", response.Headers.Location!.OriginalString);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_With_A_Valid_Operator_Code_Redirects_To_Operator()
    {
        var client = CreateClient();

        var response = await PostLogin(client, SeedCodeFor(ParticipantRole.Operator));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/operator", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_With_An_Unknown_Code_Redirects_Back_With_An_Error()
    {
        var client = CreateClient();

        var response = await PostLogin(client, "NOSUCH");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=1", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Team_Page_Redirects_To_Login_When_Unauthenticated()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/team");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Operator_Page_Denies_Access_To_A_Manager()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Manager)); // куки остаются на клиенте (HandleCookies включён по умолчанию)

        var response = await client.GetAsync("/operator");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Team_Page_Allows_A_Logged_In_Negotiator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Negotiator));

        var response = await client.GetAsync("/team");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Negotiate_Page_Allows_A_Logged_In_Negotiator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Negotiator));

        var response = await client.GetAsync("/team/negotiate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Needs_Page_Allows_A_Logged_In_Negotiator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Negotiator));

        var response = await client.GetAsync("/team/needs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Facilitator_Page_Allows_A_Logged_In_Facilitator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Facilitator));

        var response = await client.GetAsync("/facilitator");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BigScreen_Page_Allows_Anonymous_Access()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/screen");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Page_Allows_A_Logged_In_Administrator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Administrator));

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Page_Denies_Access_To_A_Manager()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Manager));

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Admin_Teams_Page_Allows_A_Logged_In_Administrator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Administrator));

        var response = await client.GetAsync("/admin/teams");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Teams_Page_Denies_Access_To_A_Manager()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Manager));

        var response = await client.GetAsync("/admin/teams");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Admin_Participants_Page_Allows_A_Logged_In_Administrator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Administrator));

        var response = await client.GetAsync("/admin/participants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Participants_Page_Denies_Access_To_A_Manager()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Manager));

        var response = await client.GetAsync("/admin/participants");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Export_Journal_Allows_A_Logged_In_Facilitator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Facilitator));

        var response = await client.GetAsync("/export/journal.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Export_Turns_Csv_Allows_A_Logged_In_Administrator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Administrator));

        var response = await client.GetAsync("/export/turns.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Export_Scores_Csv_Denies_Access_To_A_Manager()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Manager));

        var response = await client.GetAsync("/export/scores.csv");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", response.Headers.Location!.PathAndQuery);
    }

    /// <summary>
    /// Сброс нельзя прогнать через HTTP (это клик по Blazor-кнопке — интерактивность так не
    /// тестируется), поэтому дёргаем <see cref="GameSessionHost.ResetSession"/> напрямую, как
    /// <see cref="SeedTestSession"/> дёргает <see cref="GameSessionHost.StartNewSession"/>, — тот
    /// же общий fixture. Безопасно для остальных тестов файла: сохраняет те же имена команд и коды
    /// участников, которые единственно и использует остальной файл.
    /// </summary>
    [Fact]
    public void ResetSession_Preserves_Teams_And_Codes_But_Starts_A_Fresh_Journal()
    {
        var host = _factory.Services.GetRequiredService<GameSessionHost>();
        if (host.Session is null)
        {
            SeedTestSession(host);
        }

        var teamNamesBefore = host.Session!.State.Teams.Values.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var facilitatorCode = SeedCodeFor(ParticipantRole.Facilitator);
        var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");

        host.ResetSession(preset);

        var teamNamesAfter = host.Session!.State.Teams.Values.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(teamNamesBefore, teamNamesAfter);
        Assert.Equal(1, host.Session!.State.CurrentTurn);
        var found = host.Session!.TryAuthenticate(facilitatorCode);
        Assert.NotNull(found);
        Assert.Equal(ParticipantRole.Facilitator, found!.Role);
    }

    /// <summary>
    /// Черновик команд до старта сессии живёт на <see cref="GameSessionHost"/>, а не как локальное
    /// состояние Blazor-компонента — переживает пересоздание компонента (обновление страницы,
    /// переход между `/admin` и `/admin/teams`). Независим от <see cref="GameSessionHost.Session"/>,
    /// поэтому безопасен для общего на класс `_factory`, в отличие от <c>HardReset</c>.
    /// </summary>
    [Fact]
    public void StagedTeams_Are_Held_By_The_Host_Independently_Of_Any_Component()
    {
        var host = _factory.Services.GetRequiredService<GameSessionHost>();

        host.AddStagedTeam("Тестовая", "A", 500m);
        var staged = host.StagedTeams.Single(t => t.Name == "Тестовая");

        Assert.Equal("A", staged.SectorId);
        Assert.Equal(500m, staged.StartingLoanAmount);

        host.RemoveStagedTeam(staged.Id);

        Assert.DoesNotContain(host.StagedTeams, t => t.Id == staged.Id);
    }

    /// <summary>
    /// Смена чернового конфига (загрузка своего файла / тренировочный) чистит уже заведённые
    /// черновые команды — их сектора могли быть заданы под секторы старого конфига и потеряли бы
    /// смысл. Тот же приём, что <see cref="ResetSession_Preserves_Teams_And_Codes_But_Starts_A_Fresh_Journal"/>:
    /// не трогает <see cref="GameSessionHost.Session"/>, поэтому безопасен для общего `_factory`.
    /// </summary>
    [Fact]
    public void SetDraftConfig_Replaces_Config_And_Clears_Staged_Teams()
    {
        var host = _factory.Services.GetRequiredService<GameSessionHost>();
        host.AddStagedTeam("Черновая", "A", 100m);

        host.SetDraftConfig(host.TrainingConfig);

        Assert.Same(host.TrainingConfig, host.DraftConfig);
        Assert.Empty(host.StagedTeams);
    }

    [Fact]
    public async Task Print_ContractForm_Allows_A_Logged_In_Administrator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Administrator));

        var response = await client.GetAsync("/print/contract-form");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Print_TeamMemo_Allows_A_Logged_In_Facilitator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Facilitator));

        var response = await client.GetAsync("/print/team-memo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Print_ContractForm_Denies_Access_To_A_Manager()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Manager));

        var response = await client.GetAsync("/print/contract-form");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", response.Headers.Location!.PathAndQuery);
    }
}
