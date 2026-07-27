using System.Security.Claims;
using System.Text;
using Game.Domain;
using Game.Engine;
using Game.Web;
using Game.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

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
builder.Services.AddHostedService<PhaseTimerBackgroundService>();

var app = builder.Build();

// Форсируем создание сразу при старте — код администратора (до старта сессии) или коды резюмированной
// сессии (после перезапуска процесса) должны быть в логах до первого запроса.
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

    // До старта сессии участников ещё нет — сверяться не с чем; единственный ход входа тогда —
    // одноразовый код-бутстрап администратора, живущий вне журнала (Блок 9.8, GameSessionHost).
    ParticipantRegistration? registration;
    lock (host.SyncRoot)
    {
        registration = host.Session is null
            ? (host.AdminBootstrapCode is not null && code == host.AdminBootstrapCode
                ? new ParticipantRegistration(code, ParticipantRole.Administrator, null, "Администратор")
                : null)
            : host.Session.TryAuthenticate(code);
    }

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

    return Results.Redirect(RoleRouting.HomeRoute(registration.Role));
});

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// Экспорт дебрифа (Блок 10.1, SPEC §12) — обычные endpoint'ы, а не Blazor-обработчики: нужен
// Content-Disposition, которого не сделать из интерактивного компонента (та же причина, что у /auth/login).
var exportAuthorization = new AuthorizeAttribute { Roles = "Facilitator,Administrator" };

app.MapGet("/export/journal.json", (GameSessionHost host) =>
{
    lock (host.SyncRoot)
    {
        if (host.Session is null)
        {
            return Results.NotFound();
        }

        var json = JournalExport.ToJson(host.Session.Entries);
        return Results.File(Encoding.UTF8.GetBytes(json), "application/json", "journal.json");
    }
}).RequireAuthorization(exportAuthorization);

app.MapGet("/export/turns.csv", (GameSessionHost host) =>
{
    lock (host.SyncRoot)
    {
        if (host.Session is null)
        {
            return Results.NotFound();
        }

        var csv = CsvExport.TurnsToCsv(TurnHistoryCalculator.Summarize(host.Session.Entries, host.Session.State.Config));
        return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", "turns.csv");
    }
}).RequireAuthorization(exportAuthorization);

app.MapGet("/export/scores.csv", (GameSessionHost host) =>
{
    lock (host.SyncRoot)
    {
        if (host.Session is null)
        {
            return Results.NotFound();
        }

        var state = host.Session.State;
        var scores = state.Teams.Values
            .Select(team => (team.Name, FinalScoreCalculator.Calculate(team, state.Market, state.Config.Raw.Economy, state.Config.Raw.FactoryDefinitions)))
            .ToList();
        var csv = CsvExport.ScoresToCsv(scores);
        return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", "scores.csv");
    }
}).RequireAuthorization(exportAuthorization);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Точка входа — публичный частичный класс, чтобы <c>WebApplicationFactory&lt;Program&gt;</c> мог поднять приложение в тестах.</summary>
public partial class Program;
