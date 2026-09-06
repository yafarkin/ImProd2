using Game.Bots;
using Game.Domain;
using Game.Engine;

namespace Game.Balancing.Tests;

/// <summary>
/// Направление D плана исследований (<c>docs/rebalance-2sector/balance-experiment-plan.md</c>,
/// 2026-08-24): «достижим ли идеал вообще какой-либо стратегией, или он тоже отчасти нарисован
/// неправильно» — постоянная проверка того, что <see cref="IdealHallCalculator"/> действительно
/// верхняя граница, не просто удобное число для сравнения. Прогоняет реального <see cref="SimpleBot"/>
/// по всей сетке <c>leverage</c>×<c>profile</c> (0..1 по обеим осям — весь диапазон, который бот вообще
/// умеет выражать, см. doc-comment <see cref="SimpleBot"/>) и следит за сходимостью
/// (<c>Score(T)/X(T)</c>) в каждой ячейке.
/// <para>
/// <b>Порог с 2026-09-06 — 110%, а не 100%, и это осознанно.</b> Прежнее обоснование строгой верхней
/// границы («нет займа и процента на минус, поэтому потратить раньше не может быть хуже, чем позже —
/// значит стратегия зала не эвристика, а физический максимум») держалось на том, что построенная
/// фабрика ничего не стоит сверх разового <c>BuildCost</c>. После починки учёта
/// (<c>docs/economy-accounting-audit.md</c>, дефект 2) это больше не так: зал платит за наём при
/// постройке и за электричество по факту выпуска, поэтому фабрика, построенная в тот же ход, когда
/// разблокирована, но работающая недогруженной, теперь ЕЖЕХОДНО тянет вниз. «Строить всё сразу и
/// вкладывать 100% потолка каждый ход» — снова эвристика, которую в принципе можно обыграть, и на
/// этой синтетической цепочке бот её ровно и обгоняет в одной ячейке (102%). Экономическое решение о
/// постройке в самом зале — <c>docs/TODO.md</c> №29; до него X(t) — эталонная траектория, а не
/// доказанная граница, и тест сторожит только то, что бот не улетает от неё в разы.
/// </para>
/// </summary>
public class IdealHallUpperBoundTests
{
    [Fact]
    public void No_Leverage_Profile_Combination_Beats_The_Ideal_Hall()
    {
        // Числа реального фикса направления B (debug-minimal.json, эта же ветка) — не произвольные,
        // заведомо здоровая (окупающаяся) цепочка, чтобы сходимость была содержательной величиной, а
        // не постоянным null из-за X(T)<=0 (см. doc-comment BalancingHarness.ComputeAverageConvergence).
        var levels = new[]
        {
            new SyntheticChainConfigBuilder.LevelParams(BuildCost: 350m, FixedCostPerTurn: 6.67m, ProductionRate: 50m),
            new SyntheticChainConfigBuilder.LevelParams(BuildCost: 800m, FixedCostPerTurn: 20m, ProductionRate: 20m),
            new SyntheticChainConfigBuilder.LevelParams(BuildCost: 1650m, FixedCostPerTurn: 46.67m, ProductionRate: 7.5m),
        };
        var config = SyntheticChainConfigBuilder.Build(levels, inputQuantityPerLevel: 2m);
        var sector = config.Sectors.Single();
        var idealHall = IdealHallCalculator.Calculate(config, maxTurns: 90);

        var leverageLevels = StrategyGridRunner.UniformLevels(3);
        var profileLevels = StrategyGridRunner.UniformLevels(3);

        var results = StrategyGridRunner.Run(leverageLevels, profileLevels, sessionsPerCell: 3, (leverage, profile, sessionIndex) =>
        {
            var teamId = Ulid.NewUlid();
            var teams = new List<TeamSpec> { new() { Id = teamId, Name = "Бот", SectorId = sector.Id } };
            var bots = new List<SimpleBot> { new(teamId, sector, config, leverage: leverage, profile: profile) };
            var session = GameSession.StartWithEndTurn(config, "short", endTurn: 90, teams);
            return (session, (IReadOnlyList<SimpleBot>)bots, new Random(sessionIndex + 1));
        }, idealHall: idealHall);

        Assert.Contains(results, cell => cell.Report.OverallAverageFinalConvergence.HasValue);
        foreach (var cell in results)
        {
            if (cell.Report.OverallAverageFinalConvergence is not { } convergence)
            {
                continue;
            }

            Assert.True(
                convergence <= 1.10m,
                $"leverage={cell.Leverage}, profile={cell.Profile}: сходимость {convergence:P1} превышает 110% — " +
                "реальный бот обгоняет эталонную траекторию зала не на считанные проценты, а в разы; " +
                "см. doc-comment теста и docs/economy-accounting-audit.md.");
        }
    }
}
