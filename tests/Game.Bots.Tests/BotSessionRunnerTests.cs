using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Прогон полной партии простыми ботами (Блок 7.1, BUILD_PLAN «Фаза 7»): готово, когда партия из
/// 8 ботов на пилотном конфиге проходит полную сессию без вмешательства извне.
/// </summary>
public class BotSessionRunnerTests
{
    [Fact]
    public void Eight_Bots_Complete_A_Full_Session_On_The_Pilot_Config_Without_Any_Intervention()
    {
        var config = PilotBotSession.LoadConfig();
        var (session, bots) = PilotBotSession.StartEightBotSession(config, endTurn: 15);

        BotSessionRunner.RunToCompletion(session, bots, new Random(1));

        Assert.True(session.State.IsFinished);
        Assert.True(session.VerifyIntegrity());

        var changes = session.Entries.Select(e => e.Change).ToList();
        Assert.Contains(changes, c => c is MaterialSoldToSystem); // "продают системе"
        Assert.Contains(changes, c => c is FactoryBuilt); // "строят добычу" (и весь передел за ней)
        Assert.DoesNotContain(changes, c => c is DeliveryMissed); // боты всегда успевают накопить объём к поставке
        // Контракты стакана (Блок 7.3.1) здесь намеренно не проверяются: секторы A/Б этого конфига
        // экономически независимы (ни один рецепт не требует материала другого сектора), а каждый бот
        // и так строит себе полную вертикаль сектора — торговать друг с другом ботам искренне нечем.
        // Обмен между секторами со взаимными зависимостями проверяет CrossSectorTradingTests.

        // Ни у одной команды партия не должна закончиться в глубоком минусе — экономика элементарно сходится.
        foreach (var bot in bots)
        {
            Assert.True(session.State.Teams[bot.TeamId].Balance > -10_000m);
        }
    }

    [Fact]
    public void BuildFactory_Throws_When_The_Factory_Definition_Belongs_To_Another_Sector()
    {
        var config = PilotBotSession.LoadConfig();
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15,
            new[] { new TeamSpec { Id = teamId, Name = "Команда А", SectorId = sectorA.Id } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Settlement -> Decision

        Assert.Throws<ArgumentException>(() => session.BuildFactory(teamId, "oil-well"));
    }
}
