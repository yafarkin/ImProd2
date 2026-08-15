using Game.Config.Loading;
using Game.Domain;
using Game.Engine;

namespace Game.Bots.Tests;

/// <summary>
/// Регрессионный контроль «инструмент/движок сам по себе не вносит асимметрию между секторами» —
/// сессия 2026-08-15, `docs/TODO.md` №2. `control-twin-metallurgy.json` — два ЗЕРКАЛЬНО одинаковых
/// сектора (полная копия `metallurgy.json` под id `-2`), с тремя симметричными межсекторными связями
/// (одинаковое количество, одинаковый уровень передела, в обе стороны разом — не тронуть только одну
/// сторону). Если когда-нибудь `SimpleBot`/`IdealHallCalculator`/`StrategyGridRunner` неявно начнут
/// отдавать предпочтение одному сектору (например, порядку команд, порядку обхода `config.Sectors`,
/// asymmetричной обработке первого/второго продавца на рынке) — этот тест первым покраснеет, ещё до
/// того, как кто-то долго перепроверял реальный `production-model` файл на баг конфига вместо бага
/// инструмента (именно так и было потрачено много времени в сессии, которая завела этот тест).
/// </summary>
public class SectorSymmetryRegressionTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "Samples", "production-models", "control-twin-metallurgy.json");
    private static string SessionPath => Path.Combine(AppContext.BaseDirectory, "Samples", "sessions", "pilot.json");

    [Fact]
    public void IdealHall_Gives_Byte_For_Byte_Identical_Trajectories_For_Both_Mirror_Sectors()
    {
        var config = GameConfigLoader.LoadFromFiles(ConfigPath, SessionPath);
        var preset = config.Raw.SessionPresets.Single(p => p.Id == "short");

        var idealHall = IdealHallCalculator.Calculate(config, preset.MaxTurns);

        var branchA = idealHall.Branches.Single(b => b.SectorId == "A");
        var branchB = idealHall.Branches.Single(b => b.SectorId == "B");
        Assert.Equal(branchA.ValueByTurn, branchB.ValueByTurn);
    }

    [Fact]
    public void Bot_Grid_Shows_No_Convergence_Spread_Between_Two_Mirror_Sectors()
    {
        var config = GameConfigLoader.LoadFromFiles(ConfigPath, SessionPath);
        var preset = config.Raw.SessionPresets.Single(p => p.Id == "short");
        var idealHall = IdealHallCalculator.Calculate(config, preset.MaxTurns);

        var leverageLevels = new[] { 0m, 0.5m, 1m };
        var profileLevels = new[] { 0m, 0.5m, 1m };
        const int sessionsPerCell = 5;

        var results = StrategyGridRunner.Run(leverageLevels, profileLevels, sessionsPerCell, (leverage, profile, sessionIndex) =>
        {
            var teams = new List<TeamSpec>();
            var bots = new List<SimpleBot>();
            foreach (var sector in config.Sectors)
            {
                var teamId = Ulid.NewUlid();
                teams.Add(new TeamSpec { Id = teamId, Name = $"{sector.Id}-0", SectorId = sector.Id });
                bots.Add(new SimpleBot(teamId, sector, config, leverage: leverage, profile: profile));
            }

            var seed = (int)(leverage * 1000) * 100_000 + (int)(profile * 1000) * 1000 + sessionIndex;
            var session = GameSession.Start(config, preset, teams, new Random(seed + 1));
            return (session, (IReadOnlyList<SimpleBot>)bots, new Random(seed + 1_000_000));
        }, progress => { }, idealHall);

        Assert.All(results, cell =>
            Assert.True(
                cell.Report.AverageFinalConvergenceSpread is null or < 0.001m,
                $"leverage={cell.Leverage}, profile={cell.Profile}: разброс между зеркальными секторами " +
                $"{cell.Report.AverageFinalConvergenceSpread:P2} — инструмент отдаёт предпочтение одному " +
                "сектору без всякой содержательной причины (сектора идентичны по построению)."));
    }
}
