using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>Общий вход в реальный пилотный конфиг для тестов ботов и харнесса балансировки (Блоки 7.1-7.2).</summary>
internal static class PilotBotSession
{
    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "Samples", "gameconfig.pilot.json");

    public static ResolvedGameConfig LoadConfig() => GameConfigLoader.LoadFromFile(ConfigPath);

    /// <summary>4 команды сектора А + 4 сектора Б — по одному <see cref="SimpleBot"/> на команду.</summary>
    public static (GameSession Session, IReadOnlyList<SimpleBot> Bots) StartEightBotSession(ResolvedGameConfig config, int endTurn)
    {
        var sectorA = config.Sectors.Single(s => s.Id == "A");
        var sectorB = config.Sectors.Single(s => s.Id == "B");

        var teams = new List<TeamSpec>();
        var bots = new List<SimpleBot>();
        for (var i = 0; i < 8; i++)
        {
            var sector = i % 2 == 0 ? sectorA : sectorB;
            var teamId = Ulid.NewUlid();
            teams.Add(new TeamSpec { Id = teamId, Name = $"Бот {i}", SectorId = sector.Id, StartingLoanAmount = 10_000m });
            bots.Add(new SimpleBot(teamId, sector, config));
        }

        var session = GameSession.StartWithEndTurn(config, "short", endTurn, teams);
        return (session, bots);
    }
}
