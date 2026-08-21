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

// Мероприятие проходит на локальном Wi-Fi (SPEC §1) — клиенты подключаются с телефонов по LAN, не с
// той же машины, поэтому сервер обязан слушать все интерфейсы, а не только localhost.
builder.WebHost.UseUrls("http://0.0.0.0:5180");

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

// Отладочный режим (см. doc-comment DebugModeState) — читается один раз при старте, как и остальная
// конфигурация; переключение требует перезапуска процесса, отдельного эндпоинта на него нет.
builder.Services.AddSingleton(new DebugModeState(builder.Configuration.GetValue<bool>("DebugMode")));

var app = builder.Build();

// Форсируем создание сразу при старте — код администратора (до старта сессии) или коды резюмированной
// сессии (после перезапуска процесса) должны быть в логах до первого запроса.
app.Services.GetRequiredService<GameSessionHost>();

// Метка сборки для расследования подвисаний интерфейса (project_ui_freeze_investigation) — печатается
// безусловно при каждом старте, чтобы по логу сразу было видно, что запущен билд с диагностикой
// (EnterSyncRootTimed), а не более старый процесс, ещё не подобравший последние изменения.
app.Logger.LogInformation("[diag] Диагностика подвисаний активна: лок Host.SyncRoot дольше 300мс будет отмечен предупреждением \"[diag] ...\".");

if (app.Services.GetRequiredService<DebugModeState>().Enabled)
{
    app.Logger.LogWarning("[debug] DebugMode включён (appsettings) — отладочная полоса и переключатель на любого участника видны на всех страницах. Не для реального мероприятия.");
}

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
// Общая логика для обоих способов входа по коду — обычной формы (POST, ручной ввод) и QR-ссылки
// (GET, код уже зашит в URL, см. MapGet ниже и ParticipantQr.razor). Администратор входит тем же
// путём, что и любая другая роль, — никакого отдельного обхода: см. doc-comment класса
// GameSessionHost и GameSessionHost.EnsureFirstAdministrator.
static async Task<IResult> PerformLogin(HttpContext http, GameSessionHost host, string? rawCode)
{
    var code = (rawCode ?? string.Empty).Trim().ToUpperInvariant();
    if (code.Length == 0)
    {
        return Results.Redirect("/login");
    }

    ParticipantRegistration? registration;
    lock (host.SyncRoot)
    {
        if (host.Session is not null)
        {
            registration = host.Session.TryAuthenticate(code);
        }
        else
        {
            // Сессия ещё не стартовала, но код уже мог быть роздан по QR/на бумаге во время
            // подготовки (застейдженный управляющий на /admin/teams, роль без команды на
            // /admin/participants, см. GameSessionHost.AddStagedParticipant) — код обязан работать
            // сразу, а не только после отдельного клика «Начать сессию» на /admin: иначе человек
            // получает «код не найден», хотя на самом деле просто не время. Ролевая страница сама
            // покажет «сессия готовится» — см. её собственную проверку Host.Session is null.
            var staged = host.StagedParticipants.FirstOrDefault(p => p.Code == code);
            registration = staged is null
                ? null
                : new ParticipantRegistration(staged.Code, staged.Role, staged.TeamId, staged.DisplayName);
        }
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

    // ?welcome=1 — разовая памятка о роли (Блок 9.8, SPEC §3), показывается только сразу после
    // входа, не при обычных заходах на страницу после обновления/переподключения.
    return Results.Redirect($"{RoleRouting.HomeRoute(registration.Role)}?welcome=1");
}

app.MapPost("/auth/login", async (HttpContext http, GameSessionHost host) =>
{
    var form = await http.Request.ReadFormAsync();
    return await PerformLogin(http, host, form["code"].ToString());
});

// QR-код, который сразу авторизует конкретного участника (SPEC §16 «точный формат
// QR-аутентификации», SPEC §3 doc-comment «рендер в QR-картинку — отдельная надстройка»): код
// зашит прямо в ссылку (см. ParticipantQr.razor), сканирование телефоном сразу логинит, без
// ручного ввода. GET, а не POST, — сама ссылка и есть весь запрос, без формы на странице.
app.MapGet("/auth/login", (HttpContext http, GameSessionHost host, string? code) => PerformLogin(http, host, code));

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// Полный сброс (Блок «Сбросить всё в ноль») — обычный endpoint, а не Blazor-обработчик: та же
// причина, что у /auth/login и /auth/logout, плюс сам сброс должен ещё и разлогинить того, кто его
// нажал, — без этого его собственная кука авторизации осталась бы рабочей после того, как данные
// за ней исчезли, и «сброс в ноль» не ощущался бы как настоящий запуск с нуля.
app.MapPost("/admin/hard-reset", async (HttpContext http, GameSessionHost host) =>
{
    host.HardReset();
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization(new AuthorizeAttribute { Roles = "Administrator" });

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
        var materialCosts = MaterialCostCalculator.CalculateAll(state.Config);
        var scores = state.Teams.Values
            .Select(team => (team.Name, FinalScoreCalculator.Calculate(team, materialCosts, state.Config.Raw.Economy, state.Config.Raw.FactoryDefinitions)))
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
