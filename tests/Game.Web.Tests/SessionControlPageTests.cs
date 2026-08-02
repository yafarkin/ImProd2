using System.Net;
using Game.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Экран /session (запрос пользователя «разделить режим администратора — конфигурирование
/// отдельно, управление сессией отдельно») — доступен и администратору, и ведущему; до старта
/// сессии кнопка «Начать сессию» видна только администратору. Изолированная фабрика +
/// <see cref="GameSessionHost.HardReset"/>, чистит за собой в конце — тот же приём и та же причина,
/// что и у <see cref="TeamPageFactoryChainTests"/> (см. её doc-comment и <see cref="AssemblyFixture"/>).
/// </summary>
public class SessionControlPageTests
{
    [Fact]
    public async Task Session_Page_Shows_The_Start_Button_Only_To_The_Administrator_Before_The_Session_Starts()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var adminCode = host.AdminCode!;

            var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await adminClient.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = adminCode }));
            var adminHtml = await (await adminClient.GetAsync("/session")).Content.ReadAsStringAsync();
            Assert.Contains("Начать сессию", adminHtml);

            var facilitatorSpec = host.AddStagedParticipant(ParticipantRole.Facilitator, null, "Ведущий Дзета");

            var facilitatorClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await facilitatorClient.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = facilitatorSpec.Code }));
            var facilitatorResponse = await facilitatorClient.GetAsync("/session");
            Assert.Equal(HttpStatusCode.OK, facilitatorResponse.StatusCode);
            var facilitatorHtml = await facilitatorResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Начать сессию", facilitatorHtml);
            Assert.Contains("ждите администратора", facilitatorHtml);
        }
        finally
        {
            host.HardReset();
        }
    }

    [Fact]
    public async Task Session_Page_Shows_The_Transition_History_Once_The_Session_Has_Started()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var sectorId = host.DefaultConfig.Sectors.First().Id;
            host.AddStagedTeam("Эта", sectorId);
            var team = host.StagedTeams.Single();
            host.AddStagedParticipant(ParticipantRole.Manager, team.Id, "Управляющий Эта");
            var preset = host.DefaultConfig.Raw.SessionPresets.Single(p => p.Id == "short");
            host.StartSessionFromDraft(preset);

            var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await adminClient.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = host.AdminCode! }));

            var response = await adminClient.GetAsync("/session");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // Blazor Server кодирует не-ASCII символы в динамически вычисленном тексте (не в
            // статичной разметке .razor-файла) числовыми HTML-сущностями при пререндере — визуально
            // не отличить, но проверять кириллицу из C#-выражений строкой нужно после декодирования.
            var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Contains("Сессия начата", html);
            Assert.Contains("История переходов", html);
            Assert.Contains("Управляющий: ✓", html);
        }
        finally
        {
            host.HardReset();
        }
    }
}
