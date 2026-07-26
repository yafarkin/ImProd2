using System.Security.Claims;
using Game.Domain;
using Game.Web;
using Game.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Кука, а не состояние Blazor circuit, — то, что переживает обрыв circuit при блокировке
        // телефона (SPEC §11): новый circuit после реконнекта читает ту же куку и восстанавливает
        // ту же личность, не спрашивая код заново.
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });

builder.Services.AddSingleton<GameSessionHost>();

var app = builder.Build();

// Форсируем создание сразу при старте — коды входа должны быть в логах/на /dev/codes до первого запроса.
app.Services.GetRequiredService<GameSessionHost>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// Вход по коду — обычный (не Blazor) endpoint под отдельным путём /auth/...: MapRazorComponents
// уже само по себе регистрирует обработку POST на путь страницы "/login" (расширенная навигация
// форм), так что MapPost("/login", ...) сталкивается с ним же — AmbiguousMatchException. Заодно
// HttpContext.SignInAsync нельзя вызвать из интерактивного Blazor Server компонента после того, как
// ответ уже начат через SignalR circuit, — так что это в любом случае обязан быть отдельный endpoint.
app.MapPost("/auth/login", async (HttpContext http, GameSessionHost host) =>
{
    var form = await http.Request.ReadFormAsync();
    var code = form["code"].ToString().Trim().ToUpperInvariant();

    var registration = host.Session.TryAuthenticate(code);
    if (registration is null)
    {
        return Results.Redirect("/login?error=1");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.Role, registration.Role.ToString()),
        new(ClaimTypes.Name, registration.DisplayName),
        new("code", registration.Code),
    };
    if (registration.TeamId is { } teamId)
    {
        claims.Add(new Claim("teamId", teamId.ToString()));
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    var redirectTo = registration.Role switch
    {
        ParticipantRole.Manager or ParticipantRole.Negotiator => "/team",
        ParticipantRole.Operator => "/operator",
        _ => "/",
    };
    return Results.Redirect(redirectTo);
});

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Точка входа — публичный частичный класс, чтобы <c>WebApplicationFactory&lt;Program&gt;</c> мог поднять приложение в тестах.</summary>
public partial class Program;
