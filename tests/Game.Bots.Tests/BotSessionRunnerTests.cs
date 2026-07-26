using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Прогон полной партии простыми ботами (Блок 7.1, BUILD_PLAN «Фаза 7»): готово, когда партия из
/// 8 ботов на пилотном конфиге проходит полную сессию без вмешательства извне.
/// </summary>
public class BotSessionRunnerTests
{
    private static string SampleConfigPath => Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");

    /// <summary>4 команды сектора А + 4 сектора Б — по одному <see cref="SimpleBot"/> на команду.</summary>
    private static (GameSession Session, IReadOnlyList<SimpleBot> Bots) StartEightBotSession(ResolvedGameConfig config)
    {
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");

        var teams = new List<TeamSpec>();
        var bots = new List<SimpleBot>();
        for (var i = 0; i < 4; i++)
        {
            var sector = i % 2 == 0 ? sectorA : sectorB;
            var teamId = Ulid.NewUlid();
            teams.Add(new TeamSpec { Id = teamId, Name = $"Бот А{i}", SectorId = sector.Id, StartingLoanAmount = 10_000m });
            bots.Add(new SimpleBot(teamId, sector, config));
        }
        for (var i = 0; i < 4; i++)
        {
            var sector = i % 2 == 0 ? sectorA : sectorB;
            var teamId = Ulid.NewUlid();
            teams.Add(new TeamSpec { Id = teamId, Name = $"Бот Б{i}", SectorId = sector.Id, StartingLoanAmount = 10_000m });
            bots.Add(new SimpleBot(teamId, sector, config));
        }

        var session = GameSession.StartWithEndTurn(config, "short", endTurn: 15, teams);
        return (session, bots);
    }

    [Fact]
    public void Eight_Bots_Complete_A_Full_Session_On_The_Pilot_Config_Without_Any_Intervention()
    {
        var config = GameConfigLoader.LoadFromFile(SampleConfigPath);
        var (session, bots) = StartEightBotSession(config);

        BotSessionRunner.RunToCompletion(session, bots, new Random(1));

        Assert.True(session.State.IsFinished);
        Assert.True(session.VerifyIntegrity());

        var changes = session.Entries.Select(e => e.Change).ToList();
        Assert.Contains(changes, c => c is MaterialSoldToSystem); // "продают системе"
        Assert.Contains(changes, c => c is ContractDelivered); // "простые контракты"
        Assert.Contains(changes, c => c is FactoryBuilt); // "строят добычу" (и весь передел за ней)
        Assert.DoesNotContain(changes, c => c is DeliveryMissed); // боты всегда успевают накопить объём к поставке

        // Ни у одной команды партия не должна закончиться в глубоком минусе — экономика элементарно сходится.
        foreach (var bot in bots)
        {
            Assert.True(session.State.Teams[bot.TeamId].Balance > -10_000m);
        }
    }

    [Fact]
    public void BuildFactory_Throws_When_The_Factory_Definition_Belongs_To_Another_Sector()
    {
        var config = GameConfigLoader.LoadFromFile(SampleConfigPath);
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var teamId = Ulid.NewUlid();
        var session = GameSession.StartWithEndTurn(
            config, "short", endTurn: 15,
            new[] { new TeamSpec { Id = teamId, Name = "Команда А", SectorId = sectorA.Id, StartingLoanAmount = 10_000m } });
        session.AdvancePhase(PhaseTransitionTrigger.Timer); // Calculation -> Decision

        Assert.Throws<ArgumentException>(() => session.BuildFactory(teamId, "oil-well"));
    }
}
