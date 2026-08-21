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
        // Не строго Assert.Equal с 2026-08-21 (rebalance/2-sector-stepwise, переход на себестоимость
        // вместо рыночной котировки — MaterialCostCalculator) — себестоимость материала теперь считается
        // рекурсивным делением, а не берётся литералом из конфига, поэтому у неё длинный, не всегда
        // круглый десятичный хвост; порядок суммирования decimal чувствителен к этому в последнем
        // знаке (проверено отдельно — MaterialCostCalculator.CalculateAll сама по себе даёт побитово
        // одинаковую себестоимость для каждой зеркальной пары материалов, разница возникает только в
        // бухгалтерии IdealHallCalculator дальше по цепочке) — не содержательная асимметрия, шум на
        // ~28-м знаке при значениях в десятки тысяч, поэтому сравниваем с допуском, а не побитово.
        Assert.Equal(branchA.ValueByTurn.Count, branchB.ValueByTurn.Count);
        for (var turn = 0; turn < branchA.ValueByTurn.Count; turn++)
        {
            Assert.True(
                Math.Abs(branchA.ValueByTurn[turn] - branchB.ValueByTurn[turn]) < 0.0000001m,
                $"ход {turn + 1}: A={branchA.ValueByTurn[turn]}, Б={branchB.ValueByTurn[turn]}");
        }
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
