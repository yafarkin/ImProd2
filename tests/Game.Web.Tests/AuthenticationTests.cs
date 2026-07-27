using System.Net;
using Game.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>Вход по коду и разграничение доступа по роли (Блок 8.1, SPEC §3).</summary>
public class AuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private string SeedCodeFor(ParticipantRole role) =>
        _factory.Services.GetRequiredService<GameSessionHost>().SeedCodes.First(c => c.Role == role).Code;

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
}
