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
            new TeamSpec { Id = alphaId, Name = "Альфа", SectorId = sectorA.Id },
            new TeamSpec { Id = betaId, Name = "Бета", SectorId = sectorB.Id },
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
        Assert.Equal("/team?welcome=1", response.Headers.Location!.OriginalString);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_With_A_Valid_Operator_Code_Redirects_To_Operator()
    {
        var client = CreateClient();

        var response = await PostLogin(client, SeedCodeFor(ParticipantRole.Operator));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/operator?welcome=1", response.Headers.Location!.OriginalString);
    }

    /// <summary>
    /// Регрессия на исходную жалобу: раньше код администратора переставал приниматься, как только
    /// сессия стартовала (валидность была завязана на <c>Session is null</c>) — фактически второй
    /// код администратора на весь процесс. Теперь <see cref="GameSessionHost.AdminCode"/> не связан
    /// с <see cref="GameSessionHost.Session"/> и работает одинаково всегда — здесь сессия уже
    /// стартовала (сев fixture), это и есть проверяемый сценарий «после старта».
    /// </summary>
    [Fact]
    public async Task AdminCode_Logs_In_As_Administrator_Even_Though_The_Session_Has_Already_Started()
    {
        var host = _factory.Services.GetRequiredService<GameSessionHost>();
        Assert.NotNull(host.Session);
        var client = CreateClient();

        var response = await PostLogin(client, host.AdminCode!);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin?welcome=1", response.Headers.Location!.OriginalString);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Get_Auth_Login_With_Code_In_Query_Logs_In_Like_The_Post_Form()
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/auth/login?code={SeedCodeFor(ParticipantRole.Operator)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/operator?welcome=1", response.Headers.Location!.OriginalString);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>
    /// Регрессия: код застейдженного (ещё не в живой сессии) участника раньше давал «код не
    /// найден» до тех пор, пока администратор не нажимал «Начать сессию» — хотя код уже был
    /// показан по QR/на бумаге во время подготовки и должен пускать сразу. Изолированная фабрика +
    /// <see cref="GameSessionHost.HardReset"/>, как и у других тестов черновика, требующих
    /// <c>Session is null</c>.
    /// </summary>
    [Fact]
    public async Task Staged_Participant_Code_Logs_In_Before_The_Session_Has_Started()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        host.AddStagedTeam("Омега", host.DefaultConfig.Sectors.First().Id);
        var team = host.StagedTeams.Single();
        var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Омега");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = manager.Code }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/team?welcome=1", response.Headers.Location!.OriginalString);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>
    /// Регрессия: управляющий, зашедший до старта сессии по застейдженному коду (см. предыдущий
    /// тест), нажимал «Добавить переговорщика» на /team и получал «Сессия сейчас не активна» —
    /// самообслуживание было заведено только на <see cref="GameSessionHost.RegisterParticipant"/>,
    /// который требует живую сессию. Team.razor теперь в этом случае зовёт
    /// <see cref="GameSessionHost.AddStagedParticipant"/> (тот же черновик, что и у управляющего
    /// команды) — здесь проверяется именно эта комбинация целиком: застейдженный так переговорщик
    /// должен так же спокойно логиниться, как и сам управляющий.
    /// </summary>
    [Fact]
    public async Task Negotiator_Staged_By_A_Manager_Before_The_Session_Has_Started_Can_Log_In()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        host.AddStagedTeam("Сигма", host.DefaultConfig.Sectors.First().Id);
        var team = host.StagedTeams.Single();
        host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Сигма");
        var negotiator = host.AddStagedParticipant(ParticipantRole.Negotiator, team.Id, "Переговорщик Сигма");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = negotiator.Code }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/team?welcome=1", response.Headers.Location!.OriginalString);
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>
    /// Регрессия на баг из практики: черновик настройки (команды, персонал, коды входа) раньше жил
    /// только в памяти процесса (обычные <c>List&lt;&gt;</c>) и терялся при перезапуске сервера —
    /// пользователь завёл персонал на каждую роль, перезапустил (через Rider) и вкладка «Персонал»
    /// опустела. Теперь черновик — свой durable-журнал (<c>App_Data/draft</c>, см. doc-comment
    /// <see cref="GameSessionHost"/>), переживающий пересоздание процесса точно так же, как уже
    /// переживает его живая сессия. Пересоздание отдельной <see cref="WebApplicationFactory{TEntryPoint}"/>
    /// поверх того же <c>App_Data</c> — тот же приём, что и у соседних застейдженных тестов, только
    /// здесь именно два независимых экземпляра хоста, а не один и тот же на протяжении теста.
    /// </summary>
    [Fact]
    public void Staged_Draft_Survives_Recreating_The_Host_Like_A_Process_Restart()
    {
        Ulid teamId;
        StagedParticipantSpec manager;
        StagedParticipantSpec facilitator;

        using (var factory = new WebApplicationFactory<Program>())
        {
            var host = factory.Services.GetRequiredService<GameSessionHost>();
            host.HardReset();

            host.AddStagedTeam("Дельта", host.DefaultConfig.Sectors.First().Id);
            var team = host.StagedTeams.Single();
            teamId = team.Id;
            manager = host.AddStagedParticipant(ParticipantRole.Manager, teamId, "Управляющий Дельта");
            facilitator = host.AddStagedParticipant(ParticipantRole.Facilitator, null, "Ведущий");
        }

        using var restarted = new WebApplicationFactory<Program>();
        var restartedHost = restarted.Services.GetRequiredService<GameSessionHost>();

        var restartedTeam = Assert.Single(restartedHost.StagedTeams);
        Assert.Equal(teamId, restartedTeam.Id);
        Assert.Equal("Дельта", restartedTeam.Name);

        var restartedManager = restartedHost.StagedParticipants.Single(p => p.Role == ParticipantRole.Manager);
        Assert.Equal(manager.Code, restartedManager.Code);
        Assert.Equal(teamId, restartedManager.TeamId);

        var restartedFacilitator = restartedHost.StagedParticipants.Single(p => p.Role == ParticipantRole.Facilitator);
        Assert.Equal(facilitator.Code, restartedFacilitator.Code);
    }

    /// <summary>
    /// Управляющий сам заводит переговорщиков со своей страницы (самообслуживание) — тем же
    /// <see cref="GameSessionHost.RegisterParticipant"/>, что и раньше использовала только
    /// админка/оператор. UI-клик недоступен HTTP-тесту (интерактивный Blazor circuit, см.
    /// doc-comment у <c>ResetSession_...</c> ниже), поэтому проверяется то, на чём в точности стоит
    /// самообслуживание: выданный так код реально пускает в систему.
    /// </summary>
    [Fact]
    public async Task A_Newly_SelfRegistered_Negotiator_Can_Log_In()
    {
        var host = _factory.Services.GetRequiredService<GameSessionHost>();
        var alphaTeamId = host.Session!.State.Teams.Values.Single(t => t.Name == "Альфа").Id;

        var entry = host.RegisterParticipant(ParticipantRole.Negotiator, alphaTeamId, "Второй переговорщик Альфа");
        var code = ((ParticipantRegistered)entry.Change).Code;

        var client = CreateClient();
        var response = await PostLogin(client, code);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/team?welcome=1", response.Headers.Location!.OriginalString);
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

    /// <summary>
    /// «Мой код» читает код прямо из клеймов куки входа, а не из GameSessionHost — работает для
    /// любой роли и переживает даже перезапуск/сброс игровой сессии на сервере (см. запрос
    /// пользователя: «представь, что сессия вылетит»).
    /// </summary>
    [Fact]
    public async Task MyCode_Page_Shows_The_Logged_In_Managers_Own_Code()
    {
        var client = CreateClient();
        var code = SeedCodeFor(ParticipantRole.Manager);
        await PostLogin(client, code);

        var response = await client.GetAsync("/my-code");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(code, body);
    }

    [Fact]
    public async Task MyCode_Page_Redirects_To_Login_When_Unauthenticated()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/my-code");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
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

    /// <summary>Экран сессии (запрос пользователя «разделить режим администратора») общий на две роли — администратора и ведущего.</summary>
    [Fact]
    public async Task Session_Page_Allows_A_Logged_In_Administrator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Administrator));

        var response = await client.GetAsync("/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Session_Page_Allows_A_Logged_In_Facilitator()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Facilitator));

        var response = await client.GetAsync("/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Session_Page_Denies_Access_To_A_Manager()
    {
        var client = CreateClient();
        await PostLogin(client, SeedCodeFor(ParticipantRole.Manager));

        var response = await client.GetAsync("/session");

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
    /// переход между `/admin` и `/admin/teams`), и (с тех пор как черновик стал durable-журналом,
    /// <c>App_Data/draft</c>) — пересоздание самого процесса. Изолированная фабрика +
    /// <see cref="GameSessionHost.HardReset"/>, как и у остальных тестов черновика: несколько
    /// экземпляров <see cref="GameSessionHost"/> в одном тестовом процессе делят один и тот же файл
    /// на диске, и без <c>HardReset</c> в начале один экземпляр может дописать поверх файла, который
    /// другой уже переоткрыл — общий `_factory` для этого больше не годится (раньше, пока черновик
    /// был чистым `List&lt;&gt;` в памяти, каждый экземпляр был независим сам по себе).
    /// </summary>
    [Fact]
    public void StagedTeams_Are_Held_By_The_Host_Independently_Of_Any_Component()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        host.AddStagedTeam("Тестовая", "A");
        var staged = host.StagedTeams.Single(t => t.Name == "Тестовая");

        Assert.Equal("A", staged.SectorId);

        host.RemoveStagedTeam(staged.Id);

        Assert.DoesNotContain(host.StagedTeams, t => t.Id == staged.Id);
    }

    /// <summary>
    /// Смена чернового конфига (загрузка своего файла / тренировочный) чистит уже заведённые
    /// черновые команды — их сектора могли быть заданы под секторы старого конфига и потеряли бы
    /// смысл. Изолированная фабрика + <c>HardReset</c> — см. doc-comment
    /// <see cref="StagedTeams_Are_Held_By_The_Host_Independently_Of_Any_Component"/>.
    /// </summary>
    [Fact]
    public void SetDraftConfig_Replaces_Config_And_Clears_Staged_Teams()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        host.AddStagedTeam("Черновая", "A");

        host.SetDraftConfig(host.TrainingConfig);

        Assert.Same(host.TrainingConfig, host.DraftConfig);
        Assert.Empty(host.StagedTeams);
    }

    /// <summary>
    /// Черновик участников (Блок 9.8) — управляющие команд и роли без команды (админ/оператор/
    /// ведущий), заведённые до старта сессии. Изолированная фабрика + <c>HardReset</c> — см.
    /// doc-comment <see cref="StagedTeams_Are_Held_By_The_Host_Independently_Of_Any_Component"/>.
    /// </summary>
    [Fact]
    public void AddStagedParticipant_Assigns_A_Code_And_RemoveStagedTeam_Cascades_Its_Staged_Manager()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        host.AddStagedTeam("Дельта", "A");
        var team = host.StagedTeams.Single(t => t.Name == "Дельта");
        var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Дельта");
        var operatorSpec = host.AddStagedParticipant(ParticipantRole.Operator, null, "Оператор Дельта");

        Assert.False(string.IsNullOrWhiteSpace(manager.Code));
        Assert.Contains(host.StagedParticipants, p => p.Id == manager.Id);
        Assert.Contains(host.StagedParticipants, p => p.Id == operatorSpec.Id);

        host.RemoveStagedTeam(team.Id);

        Assert.DoesNotContain(host.StagedParticipants, p => p.Id == manager.Id);
        Assert.Contains(host.StagedParticipants, p => p.Id == operatorSpec.Id);

        host.RemoveStagedParticipant(operatorSpec.Id);
        Assert.DoesNotContain(host.StagedParticipants, p => p.Id == operatorSpec.Id);
    }

    /// <summary>
    /// Секторы могли смениться вместе с конфигом — застейдженные управляющие теряют смысл, роли без
    /// команды нет. Изолированная фабрика + <c>HardReset</c> — см. doc-comment
    /// <see cref="StagedTeams_Are_Held_By_The_Host_Independently_Of_Any_Component"/>.
    /// </summary>
    [Fact]
    public void SetDraftConfig_Clears_TeamScoped_Staged_Participants_But_Preserves_RoleLess_Ones()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        host.AddStagedTeam("Эпсилон", "A");
        var team = host.StagedTeams.Single(t => t.Name == "Эпсилон");
        var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Эпсилон");
        var facilitatorSpec = host.AddStagedParticipant(ParticipantRole.Facilitator, null, "Черновой ведущий");

        host.SetDraftConfig(host.TrainingConfig);

        Assert.DoesNotContain(host.StagedParticipants, p => p.Id == manager.Id);
        Assert.Contains(host.StagedParticipants, p => p.Id == facilitatorSpec.Id);
    }

    /// <summary>
    /// Требует <c>Session is null</c> (иначе <see cref="GameSessionHost.StartNewSession"/> внутри
    /// бросит) — у общего `_factory` сессия уже стартовала севом в конструкторе, поэтому здесь своя,
    /// изолированная фабрика. <see cref="GameSessionHost.HardReset"/> в начале страхует от файлов
    /// на диске, оставшихся от других тестов процесса (тот же `App_Data`, см. AGENTS.md), — гарантирует
    /// чистый черновик независимо от порядка запуска тестов.
    /// </summary>
    [Fact]
    public void StartSessionFromDraft_Commits_Staged_Teams_And_Participants_With_Their_Preassigned_Codes()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        var sectorId = host.DefaultConfig.Sectors.First().Id;
        host.AddStagedTeam("Гамма", sectorId);
        var team = host.StagedTeams.Single();
        var manager = host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Гамма");
        var operatorSpec = host.AddStagedParticipant(ParticipantRole.Operator, null, "Оператор Черновик");
        var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");

        host.StartSessionFromDraft(preset);

        Assert.NotNull(host.Session);
        var managerRegistration = host.Session!.TryAuthenticate(manager.Code);
        Assert.NotNull(managerRegistration);
        Assert.Equal(ParticipantRole.Manager, managerRegistration!.Role);
        Assert.Equal(team.Id, managerRegistration.TeamId);

        var operatorRegistration = host.Session!.TryAuthenticate(operatorSpec.Code);
        Assert.NotNull(operatorRegistration);
        Assert.Equal(ParticipantRole.Operator, operatorRegistration!.Role);

        Assert.Empty(host.StagedTeams);
        Assert.Empty(host.StagedParticipants);
    }

    /// <summary>
    /// Правило «нельзя начать сессию без единой команды» — прикладного слоя
    /// (<see cref="GameSessionHost.StartNewSession"/>), не движка (см. его doc-comment): движок
    /// (<see cref="GameSession.StartWithEndTurn"/>) намеренно позволяет пустой список команд — этим
    /// пользуются юнит-тесты Game.Engine.Tests, которым команды не нужны для проверяемой механики.
    /// Регрессия на реальный баг: раньше страница администратора реально позволяла нажать «Начать
    /// сессию» без единой заведённой команды и без единого участника.
    /// </summary>
    [Fact]
    public void StartNewSession_Throws_When_Teams_Is_Empty()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");

        Assert.Throws<ArgumentException>(() => host.StartNewSession(host.DefaultConfig, preset, Array.Empty<TeamSpec>()));
        Assert.Null(host.Session);
    }

    /// <summary>То же правило, но через реальный путь администратора — старт из черновика без единой заведённой команды.</summary>
    [Fact]
    public void StartSessionFromDraft_Throws_When_No_Teams_Are_Staged()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");

        Assert.Throws<ArgumentException>(() => host.StartSessionFromDraft(preset));
        Assert.Null(host.Session);
    }

    /// <summary>
    /// Регрессия на реальный баг: команду можно было завести и стартовать сессию, не назначив ей
    /// управляющего — без него команда неиграбельна (только он может заводить остальной состав
    /// самообслуживанием и подтверждать сделки). Черновик после ошибки не трогается — можно
    /// доназначить управляющего и повторить попытку без потери уже введённых данных.
    /// </summary>
    [Fact]
    public void StartSessionFromDraft_Throws_When_A_Team_Has_No_Manager()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        var sectorId = host.DefaultConfig.Sectors.First().Id;
        host.AddStagedTeam("Дзета", sectorId);
        var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");

        var ex = Assert.Throws<InvalidOperationException>(() => host.StartSessionFromDraft(preset));
        Assert.Contains("Дзета", ex.Message);
        Assert.Null(host.Session);
        Assert.Single(host.StagedTeams);

        var team = host.StagedTeams.Single();
        host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Дзета");

        host.StartSessionFromDraft(preset);

        Assert.NotNull(host.Session);
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
