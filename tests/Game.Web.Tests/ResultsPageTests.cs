using System.Net;
using Game.Config.Loading;
using Game.Domain;
using Game.Engine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Web.Tests;

/// <summary>
/// Экран итогов игры /results и объявление конца партии (docs/TODO.md №10). До этих правок момент
/// окончания вообще никак не проявлялся в интерфейсе: <see cref="GameSessionState.IsFinished"/> не
/// читала ни одна игровая страница, а итоговый счёт был доступен только выгрузкой CSV — люди
/// расходились, не увидев результата.
///
/// <para>
/// Изолированная фабрика + <see cref="GameSessionHost.HardReset"/> до и после — тот же приём и по
/// той же причине, что у <see cref="TeamPageAttentionTests"/> (общий физический <c>App_Data</c>,
/// см. <see cref="AssemblyFixture"/>).
/// </para>
/// </summary>
public class ResultsPageTests
{
    private static ResolvedGameConfig FixtureConfig() => GameConfigLoader.LoadFromFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "production-models", "tiny-2-sectors.json"),
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json"));

    private sealed record Setup(GameSessionHost Host, Ulid FirstTeamId, string ManagerCode);

    /// <summary>Две команды (чтобы таблица итогов была таблицей, а не одной строкой) и управляющий у первой из них.</summary>
    private static Setup StartSession(GameSessionHost host)
    {
        host.SetDraftConfig(FixtureConfig());
        var sectorId = host.DraftConfig.Sectors.First().Id;
        host.AddStagedTeam("Каппа", sectorId);
        host.AddStagedTeam("Лямбда", sectorId);
        var firstTeam = host.StagedTeams.First(team => team.Name == "Каппа");
        var secondTeam = host.StagedTeams.First(team => team.Name == "Лямбда");
        var manager = host.AddStagedParticipant(ParticipantRole.Manager, firstTeam.Id, "Управляющий Каппа");
        // Управляющий обязателен у каждой команды — без него сессия не стартует.
        host.AddStagedParticipant(ParticipantRole.Manager, secondTeam.Id, "Управляющий Лямбда");
        host.StartSessionFromDraft();

        return new Setup(host, firstTeam.Id, manager.Code);
    }

    /// <summary>
    /// Доигрывает партию до конца одними переходами фаз, без тиков: сессию завершает
    /// <see cref="PhaseAdvanced"/> на фазе решений хода <see cref="GameSessionState.EndTurn"/>, а не
    /// расчёт, — производство для проверки самого факта окончания не нужно.
    /// </summary>
    private static void PlayToTheEnd(GameSessionHost host)
    {
        while (!host.Session!.State.IsFinished)
        {
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator);
        }
    }

    private static async Task<HttpClient> LoginAs(WebApplicationFactory<Program> factory, string code)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = code }));
        return client;
    }

    private static async Task<string> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// По ходу игры экран доступен, но обязан честно сказать, что это ещё не итог, — иначе команда,
    /// заглянувшая на него на середине партии, унесёт промежуточный счёт за окончательный.
    /// </summary>
    [Fact]
    public async Task Mid_Game_The_Results_Screen_Marks_Itself_As_Preliminary()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var setup = StartSession(host);
            var client = await LoginAs(factory, setup.ManagerCode);

            var html = await Get(client, "/results");

            Assert.Contains("Партия ещё идёт", html);
            Assert.DoesNotContain("Партия завершена", html);
            // Обе команды в таблице, своя — отмечена.
            Assert.Contains("Каппа", html);
            Assert.Contains("Лямбда", html);
            Assert.Contains("badge bg-primary", html);
        }
        finally
        {
            host.HardReset();
        }
    }

    /// <summary>
    /// Разбивка «деньги / склад / фабрики» — не украшение таблицы, а её содержание: одинаковый счёт
    /// может быть набран противоположными способами, и именно об этом идёт разговор на разборе.
    /// </summary>
    [Fact]
    public void The_Results_Screen_Breaks_The_Score_Down_Into_Money_Warehouse_And_Factories()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var setup = StartSession(host);
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision
            var mineDefinitionId = host.Session!.State.Config.FactoryDefinitions
                .Single(d => d.Sector.Id == host.Session!.State.Teams[setup.FirstTeamId].Sector.Id && d.Recipes.Single().Output.Level == 0).Id;
            host.Session!.BuildFactory(setup.FirstTeamId, mineDefinitionId);

            var rows = ResultsDisplay.Rank(host.Session!);

            var withFactory = rows.Single(row => row.TeamId == setup.FirstTeamId);
            var withoutFactory = rows.Single(row => row.TeamId != setup.FirstTeamId);

            Assert.True(withFactory.FactoriesValue > 0m, "Построенная фабрика обязана попасть в счёт как актив.");
            Assert.Equal(0m, withoutFactory.FactoriesValue);
            Assert.Equal(withFactory.Cash + withFactory.WarehouseValue + withFactory.FactoriesValue, withFactory.Score);
            // Постройка списала деньги — на балансе команда беднее соперника, и весь смысл счёта в
            // том, что фабрика это компенсирует (частично: остаточная стоимость ниже цены постройки).
            Assert.True(withFactory.Cash < withoutFactory.Cash);
        }
        finally
        {
            host.HardReset();
        }
    }

    /// <summary>Равный счёт — общее место, следующая команда получает место со сдвигом (спортивное правило).</summary>
    [Fact]
    public void Teams_With_The_Same_Score_Share_A_Place()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            StartSession(host); // обе команды нетронуты — стартовые условия одинаковы

            var rows = ResultsDisplay.Rank(host.Session!);

            Assert.Equal(2, rows.Count);
            Assert.Equal(rows[0].Score, rows[1].Score);
            Assert.All(rows, row => Assert.Equal(1, row.Place));
        }
        finally
        {
            host.HardReset();
        }
    }

    /// <summary>Конец партии обязан быть виден: и на экране итогов, и на странице команды, откуда игрок туда попадёт.</summary>
    [Fact]
    public async Task When_The_Session_Finishes_Both_The_Results_Screen_And_The_Team_Page_Announce_It()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var setup = StartSession(host);
            PlayToTheEnd(host);
            Assert.True(host.Session!.State.IsFinished);

            var client = await LoginAs(factory, setup.ManagerCode);

            var results = await Get(client, "/results");
            Assert.Contains("Партия завершена", results);
            Assert.DoesNotContain("Партия ещё идёт", results);

            var team = await Get(client, "/team");
            Assert.Contains("Партия завершена", team);
            Assert.Contains("/results", team);
        }
        finally
        {
            host.HardReset();
        }
    }

    /// <summary>
    /// Панель «Требует внимания» после конца партии обязана замолчать: все её формулировки —
    /// про будущее («списывается каждый ход», «сломается через N ходов»), и после последнего хода
    /// это уже не предупреждение, а неверное утверждение о ходах, которых не будет.
    /// </summary>
    [Fact]
    public async Task The_Attention_Panel_Goes_Quiet_Once_The_Session_Has_Finished()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            var setup = StartSession(host);
            host.Session!.AdvancePhase(PhaseTransitionTrigger.Facilitator); // Settlement -> Decision
            var mineDefinitionId = host.Session!.State.Config.FactoryDefinitions
                .Single(d => d.Sector.Id == host.Session!.State.Teams[setup.FirstTeamId].Sector.Id && d.Recipes.Single().Output.Level == 0).Id;
            // Фабрика без рабочих — гарантированный повод для панели, пока партия идёт.
            host.Session!.BuildFactory(setup.FirstTeamId, mineDefinitionId);

            var client = await LoginAs(factory, setup.ManagerCode);
            var midGame = await Get(client, "/team");
            Assert.Contains("нет рабочих", midGame);
            Assert.Contains("badge rounded-pill bg-danger", midGame);

            PlayToTheEnd(host);

            var finished = await Get(client, "/team");
            Assert.Contains("предупреждать больше не о чем", finished);
            Assert.DoesNotContain("нет рабочих", finished);
            Assert.DoesNotContain("badge rounded-pill bg-danger", finished);
            // Вводный текст панели — про будущее ровно так же, как и сами поводы, и молчать обязан
            // вместе с ними: «что сломается в ближайшие 3 хода» после последнего хода бессмысленно.
            Assert.DoesNotContain("что сломается в ближайшие", finished);
        }
        finally
        {
            host.HardReset();
        }
    }

    /// <summary>
    /// Большой экран объявляет конец сам — нажать на нём «обновить» некому, а разбор в зале
    /// начинается ровно в эту минуту. По ходу игры он показывает обычный рейтинг по балансу.
    /// </summary>
    [Fact]
    public async Task The_Big_Screen_Switches_To_The_Final_Score_When_The_Session_Finishes()
    {
        using var factory = new WebApplicationFactory<Program>();
        var host = factory.Services.GetRequiredService<GameSessionHost>();
        host.HardReset();

        try
        {
            StartSession(host);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var midGame = await Get(client, "/screen");
            Assert.Contains("Рейтинг команд", midGame);
            Assert.DoesNotContain("Итоговый счёт", midGame);

            PlayToTheEnd(host);

            var finished = await Get(client, "/screen");
            Assert.Contains("Партия завершена", finished);
            Assert.Contains("Итоговый счёт", finished);
            Assert.DoesNotContain("Рейтинг команд", finished);
        }
        finally
        {
            host.HardReset();
        }
    }
}
