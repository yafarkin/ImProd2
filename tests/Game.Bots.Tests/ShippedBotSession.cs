using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Вход в <b>поставочную</b> пару конфигов (боевая цепочка <c>main-3-sectors.json</c> + сессия
/// <c>main.json</c>) для тестов, которым важен экономический исход, а не только механика: партия
/// должна сходиться на том, во что реально играют, а не на игрушечном каталоге.
///
/// <para>Отличие от <see cref="PilotBotSession"/> (<c>tests/Fixtures/legacy-combined-gameconfig.json</c>):
/// тот остаётся маленьким детерминированным каталогом для механических проверок — загрузка конфига,
/// сборка снапшота, обход сетки стратегий, — где содержание цепочки роли не играет и лишние 27 фабрик
/// только замедляли бы прогон. Экономику legacy-файла никто не калибрует и калибровать не будет
/// (<c>docs/TODO.md</c> №26): поставляется две модели и один сессионный файл, комбинированного
/// конфига среди них нет.</para>
/// </summary>
internal static class ShippedBotSession
{
    public static string ProductionModelPath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "main-3-sectors.json");

    public static string SessionPath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "main.json");

    public static ResolvedGameConfig LoadConfig() => GameConfigLoader.LoadFromFiles(ProductionModelPath, SessionPath);

    /// <summary>
    /// По <paramref name="teamsPerSector"/> команд в каждом секторе конфига, по одному
    /// <see cref="SimpleBot"/> на команду. Именно так считает и диагностика цепочки
    /// (<c>Game.Balancing --mode diagnose</c>) — конкуренция за общую ёмкость рынка внутри сектора
    /// должна быть, иначе проверяется не та экономика, что в зале.
    /// </summary>
    public static (GameSession Session, IReadOnlyList<SimpleBot> Bots) StartSession(
        ResolvedGameConfig config, int endTurn, int teamsPerSector = 2)
    {
        ArgumentNullException.ThrowIfNull(config);

        var teams = new List<TeamSpec>();
        var bots = new List<SimpleBot>();
        foreach (var sector in config.Sectors)
        {
            for (var i = 0; i < teamsPerSector; i++)
            {
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"Бот {sector.Id}{i + 1}", SectorId = sector.Id });
                bots.Add(new SimpleBot(teamId, sector, config));
            }
        }

        return (GameSession.StartWithEndTurn(config, endTurn, teams), bots);
    }
}
